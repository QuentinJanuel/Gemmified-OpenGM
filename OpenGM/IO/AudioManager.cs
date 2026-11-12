using NAudio.Wave;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenTK.Audio.OpenAL;
using StbVorbisSharp;
using System.Diagnostics;

namespace OpenGM.IO;

/*
 * copied from https://github.com/misternebula/DELTARUNITY/blob/main/Assets/Scripts/AudioManager/AudioManager.cs
 * organization is eh, spread out between here and ScriptResolver. its fine
 * 
 * resources i used:
 * https://indiegamedev.net/2020/02/15/the-complete-guide-to-openal-with-c-part-1-playing-a-sound/
 * https://gist.github.com/kamiyaowl/32fb397e0141c65792e1
 * https://www.openal.org/documentation/OpenAL_Programmers_Guide.pdf
 *
 * if openal sucks too much, could try using naudio
 */

public class AudioInstance
{
    public AudioAsset Asset = null!;
    public int SoundInstanceId;
    public int Source;
    public double Priority;
    public float Timer;

    // Backs AudioManager.ChangeGain's fade-over-time -- null means no fade is in progress.
    public double? GainLerpStartGain;
    public double GainLerpTargetGain;
    public double GainLerpTotalMs;
    public double GainLerpElapsedMs;
}

public class AudioAsset
{
    public int AssetIndex;
    public string Name = null!;
    /// <summary>OpenAL/WebAudio buffer id; 0 means not decoded yet (lazy music).</summary>
    public int Clip;
    public double Gain;
    public double Pitch;
    public double Offset;

    // Lazy audio LRU (mirrors OpenGMK AudioBufferCache policy).
    public byte[]? CompressedBytes;
    public string FileName = "";
    public double DurationSecs;
    public long ApproxDecodedBytes;
    public bool IsMusic;
    public bool Permanent;
    public int PinCount;

    /// <summary>
    /// Came from audio_create_stream, i.e. a loose file GameMaker plays *from disk* rather than
    /// holding in memory. Such an asset counts as music however short it is, and is re-read from
    /// <see cref="FileName"/> each time rather than keeping compressed bytes of its own -- see
    /// <see cref="AudioManager.RegisterStreamedFile"/>.
    /// </summary>
    public bool Streamed;
}

public class AudioCacheStatsSnapshot
{
    public int DecodedMusicCount;
    public long DecodedMusicBytes;
    public int DecodedSfxCount;
    public long CompressedBytes;
    public int PinnedCount;
    public string? LastEventType;
    public string? LastEventId;
    public double LastEventAt;
}

public static class AudioManager
{
    private static ALDevice _device;
    private static ALContext _context;

    public static int AudioChannelNum = 128;

    private static List<AudioInstance> _audioSources = new();
    private static Dictionary<int, AudioAsset> _audioClips = new();
    private static List<AudioEmitter> _audioEmitters = new();

    public static AudioListener Listener = new();

    private static bool _inited;

    // Audio cache policy -- defaults match apps/web/src/shell/audio-cache-policy.ts.
    //
    // There is no "lazy" switch any more, and that is the point: it used to gate *whether music was
    // evictable at all* (EnsureDecoded set Permanent = !IsMusic || !_lazyAudio), so with it off --
    // which is what every desktop got -- music dropped its compressed bytes and was pinned forever,
    // and the budget and the LRU below were dead code. The budget is now always enforced, and the
    // platform difference is just how big it is.
    private static int _shortCompressedBytes = 256 * 1024;
    private static double _shortDurationSecs = 5.0;
    private static long _musicBudgetBytes = 192L * 1024 * 1024;
    private static readonly LinkedList<int> _musicLru = new(); // front = least recently used
    private static string? _lastEventType;
    private static string? _lastEventId;
    private static double _lastEventAt;

    public static void Init()
    {
        if (_inited)
        {
            return;
        }

        _device = ALC.OpenDevice(null);
        CheckALCError();
        _context = ALC.CreateContext(_device, new ALContextAttributes());
        CheckALCError();
        ALC.MakeContextCurrent(_context);
        CheckALCError();

        Console.WriteLine(AL.Get(ALGetString.Version));
        CheckALError();
        //Console.WriteLine(AL.Get(ALGetString.Vendor));
        //CheckALError();
        //Console.WriteLine(AL.Get(ALGetString.Extensions));
        //CheckALError();

        _inited = true;

        ApplyInitialAudioCacheFromJs();
    }

    /// <summary>
    /// Decodes a WAV or OGG file's raw bytes (identified by its own filename's extension, matching
    /// the SND chunk's per-asset "File" field) into interleaved PCM float samples, ready for
    /// AL.BufferData. Shared by LoadSounds (embedded assets, read at game load) and the legacy
    /// sound_add/sound_replace GML functions (SoundFunctionsLegacy.cs, dynamically loaded at
    /// runtime from a loose file) so both go through the exact same decode path.
    /// </summary>
    public static (float[] Data, bool Stereo, int Freq) DecodeSoundBytes(byte[] bytes, string fileName)
    {
        float[] data;
        bool stereo;
        int freq;
        if (Path.GetExtension(fileName) == ".wav")
        {
            // write to the same file to get slightly more perf. need the extension so the reader knows what it is
            // file sucks but is needed to have read that lets us use floats
            File.WriteAllBytes("temp.wav", bytes);

            try
            {
                using var audioFileReader = new AudioFileReader("temp.wav");
                data = new float[audioFileReader.Length * 8 / audioFileReader.WaveFormat.BitsPerSample]; // taken from owml
                var realLength = audioFileReader.Read(data, 0, data.Length);
                if (realLength != data.Length)
                {
                    DebugLog.LogWarning($"{fileName} length {realLength} != {data.Length}");
                }
                stereo = audioFileReader.WaveFormat.Channels == 2;
                freq = audioFileReader.WaveFormat.SampleRate;
            }
            catch (Exception e) // i think this is caused by ch1. not sure why
            {
                DebugLog.LogWarning($"couldnt read wave file {fileName}: {e}");
                data = new float[] { };
                freq = 1;
                stereo = false;
            }
            finally
            {
                File.Delete("temp.wav");
            }
        }
        else if (Path.GetExtension(fileName) == ".ogg")
        {
            (data, stereo, freq) = DecodeVorbis(bytes, fileName);
        }
        else if (Path.GetExtension(fileName) == ".mp3")
        {
            // NLayer is pure-managed (no native decoder P/Invoke, unlike NAudio's own Mp3FileReader,
            // which relies on Windows ACM and isn't available cross-platform or under wasm), same
            // reasoning as StbVorbisSharp for .ogg above.
            using var mp3 = new NLayer.MpegFile(new MemoryStream(bytes));
            var samples = new List<float>();
            var chunk = new float[4096];
            int read;
            while ((read = mp3.ReadSamples(chunk, 0, chunk.Length)) > 0)
            {
                if (read == chunk.Length)
                {
                    samples.AddRange(chunk);
                }
                else
                {
                    samples.AddRange(chunk.AsSpan(0, read).ToArray());
                }
            }
            data = samples.ToArray();
            stereo = mp3.Channels == 2;
            freq = mp3.SampleRate;
        }
        else
        {
            throw new NotImplementedException($"unknown audio file format {fileName}");
        }

        return (data, stereo, freq);
    }

    /// <summary>
    /// stb_vorbis only understands an Ogg stream that carries Vorbis and nothing else. Some games
    /// ship music with the cover art muxed in beside the audio as a Theora video stream -- straight
    /// out of whatever tagged the file, and GameMaker plays those without complaint. stb_vorbis
    /// refuses the file outright, and StbVorbisSharp never checks whether the open succeeded, so
    /// that surfaces as a bare NullReferenceException thrown out of Vorbis.FromMemory. Left
    /// unhandled it takes down the whole load, not just the one sound (I wanna reach the Moon,
    /// whose musDog and musMK are the only two of its 127 sounds shaped like this).
    ///
    /// So: try the file as it came, and if it will not open, try again with everything but the
    /// Vorbis bitstream dropped. If even that fails, the sound plays as silence -- the same way an
    /// unreadable .wav already degrades above, since one bad track is never worth a dead game.
    /// </summary>
    private static (float[] Data, bool Stereo, int Freq) DecodeVorbis(byte[] bytes, string fileName)
    {
        if (TryDecodeVorbis(bytes, fileName, out var result))
        {
            return result;
        }

        if (TryIsolateVorbisStream(bytes, out var isolated) && TryDecodeVorbis(isolated, fileName, out result))
        {
            return result;
        }

        DebugLog.LogWarning($"couldnt read ogg file {fileName}, playing it as silence");
        return ([], false, 1);
    }

    private static bool TryDecodeVorbis(byte[] bytes, string fileName, out (float[] Data, bool Stereo, int Freq) result)
    {
        try
        {
            using var vorbis = Vorbis.FromMemory(bytes);
            var data = new float[vorbis.StbVorbis.total_samples * vorbis.Channels];
            unsafe
            {
                fixed (float* ptr = data)
                {
                    var realLength = StbVorbis.stb_vorbis_get_samples_float_interleaved(vorbis.StbVorbis, vorbis.Channels, ptr, data.Length);
                    realLength *= vorbis.Channels;
                    if (realLength != data.Length)
                    {
                        DebugLog.LogWarning($"{fileName} length {realLength} != {data.Length}");
                    }
                }
            }
            result = (data, vorbis.Channels == 2, vorbis.SampleRate);
            return true;
        }
        catch (Exception)
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Rebuilds an Ogg container keeping only the pages of its Vorbis logical bitstream.
    ///
    /// Ogg interleaves the pages of its streams but numbers them per stream, and a page's checksum
    /// covers only that page, so the kept pages are still consecutively numbered and still valid
    /// once the others are gone -- they can be copied across byte for byte with nothing rewritten.
    ///
    /// False if this is not page-aligned Ogg, has no Vorbis stream, or holds only the one stream
    /// (in which case whatever stb_vorbis choked on, dropping pages will not help).
    /// </summary>
    private static bool TryIsolateVorbisStream(byte[] bytes, out byte[] isolated)
    {
        isolated = [];

        var pages = ReadOggPages(bytes);
        if (pages == null)
        {
            return false;
        }

        var serials = new List<uint>();
        uint? vorbisSerial = null;
        foreach (var page in pages)
        {
            if (!page.BeginsStream)
            {
                continue;
            }

            if (!serials.Contains(page.Serial))
            {
                serials.Add(page.Serial);
            }

            // A Vorbis stream opens with an identification header: packet type 1, then "vorbis".
            if (vorbisSerial == null
                && page.BodyLength >= 7
                && bytes[page.BodyOffset] == 1
                && bytes[page.BodyOffset + 1] == 'v'
                && bytes[page.BodyOffset + 2] == 'o'
                && bytes[page.BodyOffset + 3] == 'r'
                && bytes[page.BodyOffset + 4] == 'b'
                && bytes[page.BodyOffset + 5] == 'i'
                && bytes[page.BodyOffset + 6] == 's')
            {
                vorbisSerial = page.Serial;
            }
        }

        if (vorbisSerial == null || serials.Count < 2)
        {
            return false;
        }

        using var stream = new MemoryStream();
        foreach (var page in pages)
        {
            if (page.Serial == vorbisSerial)
            {
                stream.Write(bytes, page.Offset, page.Length);
            }
        }

        isolated = stream.ToArray();
        return true;
    }

    private readonly record struct OggPage(int Offset, int Length, int BodyOffset, int BodyLength, uint Serial, bool BeginsStream);

    /// <summary>
    /// Walks an Ogg file's pages. Null if the bytes are not Ogg or a page runs off the end --
    /// better to hand the file back to stb_vorbis untouched than to guess at where it resyncs.
    /// </summary>
    private static List<OggPage>? ReadOggPages(byte[] bytes)
    {
        var pages = new List<OggPage>();
        var offset = 0;

        // A page is "OggS", a 22-byte header, a segment table, then the segments it sizes.
        while (offset + 27 <= bytes.Length)
        {
            if (bytes[offset] != 'O' || bytes[offset + 1] != 'g' || bytes[offset + 2] != 'g' || bytes[offset + 3] != 'S')
            {
                return null;
            }

            var segmentCount = bytes[offset + 26];
            var headerLength = 27 + segmentCount;
            if (offset + headerLength > bytes.Length)
            {
                return null;
            }

            var bodyLength = 0;
            for (var i = 0; i < segmentCount; i++)
            {
                bodyLength += bytes[offset + 27 + i];
            }

            if (offset + headerLength + bodyLength > bytes.Length)
            {
                return null;
            }

            var serial = (uint)(bytes[offset + 14] | (bytes[offset + 15] << 8) | (bytes[offset + 16] << 16) | (bytes[offset + 17] << 24));
            pages.Add(new OggPage(
                offset,
                headerLength + bodyLength,
                offset + headerLength,
                bodyLength,
                serial,
                (bytes[offset + 5] & 0x02) != 0));

            offset += headerLength + bodyLength;
        }

        return pages;
    }

    public static void SetAudioCachePolicy(int shortCompressedBytes, double shortDurationSecs, long musicBudgetBytes)
    {
        _shortCompressedBytes = shortCompressedBytes > 0 ? shortCompressedBytes : 256 * 1024;
        _shortDurationSecs = shortDurationSecs > 0 ? shortDurationSecs : 5.0;
        _musicBudgetBytes = musicBudgetBytes > 0 ? musicBudgetBytes : 192L * 1024 * 1024;
        EvictUntilUnderBudget();
    }

    public static void ApplyInitialAudioCacheFromJs()
    {
        // Preferential config is applied from WasmEntry.SetAudioCachePolicy before LoadGame.
        // Kept as a no-op hook so native builds compile unchanged if called.
    }

    private static void RecordCacheEvent(string type, string id)
    {
        _lastEventType = type;
        _lastEventId = id;
        _lastEventAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static bool ClassifyAsMusic(int compressedLen, double durationSecs)
    {
        if (compressedLen > 0 && compressedLen <= _shortCompressedBytes)
            return false;
        if (durationSecs > 0 && durationSecs <= _shortDurationSecs)
            return false;
        if (compressedLen > _shortCompressedBytes)
            return true;
        return durationSecs > _shortDurationSecs;
    }

    private static void TouchMusicLru(int assetIndex)
    {
        _musicLru.Remove(assetIndex);
        _musicLru.AddLast(assetIndex);
    }

    private static long MusicDecodedBytes()
    {
        long sum = 0;
        foreach (var asset in _audioClips.Values)
        {
            if (asset.IsMusic && !asset.Permanent && asset.Clip != 0)
            {
                sum += asset.ApproxDecodedBytes;
            }
        }
        return sum;
    }

    private static void EvictUntilUnderBudget()
    {
        while (MusicDecodedBytes() > _musicBudgetBytes)
        {
            int? victim = null;
            foreach (var id in _musicLru)
            {
                if (_audioClips.TryGetValue(id, out var a)
                    && a.Clip != 0
                    && a.PinCount == 0
                    && a.IsMusic
                    && !a.Permanent)
                {
                    victim = id;
                    break;
                }
            }
            if (victim is null)
                break;
            DiscardDecoded(victim.Value);
        }
    }

    private static void DiscardDecoded(int assetIndex)
    {
        if (!_audioClips.TryGetValue(assetIndex, out var asset))
            return;
        if (asset.PinCount > 0 || asset.Clip == 0)
            return;
        AL.DeleteBuffer(asset.Clip);
        CheckALError();
        asset.Clip = 0;
        asset.ApproxDecodedBytes = 0;
        _musicLru.Remove(assetIndex);
        RecordCacheEvent("evict", asset.Name);
    }

    /// <summary>Decode + upload PCM if needed. Returns the AL buffer id.</summary>
    public static int EnsureDecoded(AudioAsset asset)
    {
        if (asset.Clip != 0)
        {
            if (asset.IsMusic && !asset.Permanent)
                TouchMusicLru(asset.AssetIndex);
            return asset.Clip;
        }

        var bytes = asset.CompressedBytes;
        if ((bytes is null || bytes.Length == 0) && asset.Streamed)
        {
            // A stream keeps no compressed copy of its own -- the file it names is its storage, so
            // that the bytes aren't held twice (the browser's in-memory filesystem is the same wasm
            // heap everything else is competing for).
            if (!File.Exists(asset.FileName))
            {
                DebugLog.LogError($"EnsureDecoded: {asset.Name} streams from {asset.FileName}, which is gone");
                return 0;
            }
            bytes = File.ReadAllBytes(asset.FileName);
        }
        if (bytes is null || bytes.Length == 0)
        {
            DebugLog.LogError($"EnsureDecoded: no compressed bytes for {asset.Name}");
            return 0;
        }

        var (data, stereo, freq) = DecodeSoundBytes(bytes, asset.FileName);
        var buffer = AL.GenBuffer();
        CheckALError();
        AL.BufferData(buffer, stereo ? ALFormat.StereoFloat32Ext : ALFormat.MonoFloat32Ext, data, freq);
        CheckALError();

        var channels = stereo ? 2 : 1;
        var frames = channels > 0 ? data.Length / channels : 0;
        asset.DurationSecs = freq > 0 ? (double)frames / freq : 0;
        asset.ApproxDecodedBytes = (long)data.Length * 4;
        // A stream counts as music however short it is: GameMaker plays these off disk precisely
        // so they don't sit in memory, so it belongs under the budget either way.
        asset.IsMusic = asset.Streamed || ClassifyAsMusic(bytes.Length, asset.DurationSecs);
        // SFX are pinned for good (small, and a hitch on a sound effect is what you'd notice);
        // music is always evictable, on every platform.
        asset.Permanent = !asset.IsMusic;
        if (asset.Permanent)
        {
            // SFX / eager: drop compressed to free RAM once decoded.
            asset.CompressedBytes = null;
            _musicLru.Remove(asset.AssetIndex);
        }
        else
        {
            TouchMusicLru(asset.AssetIndex);
        }

        asset.Clip = buffer;
        RecordCacheEvent("decode", asset.Name);
        EvictUntilUnderBudget();
        return buffer;
    }

    public static void PinAsset(AudioAsset asset)
    {
        asset.PinCount++;
        if (asset.PinCount == 1)
            RecordCacheEvent("pin", asset.Name);
        if (asset.IsMusic && !asset.Permanent)
            TouchMusicLru(asset.AssetIndex);
    }

    public static void UnpinAsset(AudioAsset asset)
    {
        if (asset.PinCount <= 0)
            return;
        asset.PinCount--;
        if (asset.PinCount == 0)
        {
            RecordCacheEvent("unpin", asset.Name);
            EvictUntilUnderBudget();
        }
    }

    public static AudioCacheStatsSnapshot GetAudioCacheStats()
    {
        var snap = new AudioCacheStatsSnapshot();
        foreach (var asset in _audioClips.Values)
        {
            if (asset.CompressedBytes is { Length: > 0 } c)
            {
                snap.CompressedBytes += c.Length;
            }
            if (asset.PinCount > 0)
                snap.PinnedCount++;
            if (asset.Clip == 0)
                continue;
            // Count by classification, not permanence.
            if (asset.IsMusic)
            {
                snap.DecodedMusicCount++;
                snap.DecodedMusicBytes += asset.ApproxDecodedBytes;
            }
            else
            {
                snap.DecodedSfxCount++;
            }
        }
        snap.LastEventType = _lastEventType;
        snap.LastEventId = _lastEventId;
        snap.LastEventAt = _lastEventAt;
        return snap;
    }

    /// <summary>
    /// load all the audio data into buffers
    /// has to happen after init since context is set up there
    /// </summary>
    public static void LoadSounds(BinaryReader reader)
    {
        Console.Write($"Loading sounds...");

        var length = reader.ReadInt32();
        for (var i = 0; i < length; i++)
        {
            var asset = reader.ReadMemoryPack<SoundAsset>();

            var bytesLength = reader.ReadInt32();
            var bytes = reader.ReadBytes(bytesLength);

            var entry = new AudioAsset
            {
                AssetIndex = asset.AssetID,
                Name = asset.Name,
                Clip = 0,
                Gain = asset.Volume,
                Pitch = asset.Pitch,
                Offset = 0,
                CompressedBytes = bytes,
                FileName = asset.File,
            };

            // Registered before it is decoded, not after: MusicDecodedBytes and the LRU both walk
            // _audioClips, so an asset that isn't in it yet is invisible to its own eviction pass.
            _audioClips[asset.AssetID] = entry;

            // Decode up front while there's budget for it, and leave the rest for first play.
            //
            // This is what makes the budget the only knob. A game whose music fits is decoded
            // exactly as eagerly as it always was -- every track resident before the first frame,
            // no first-play hitch anywhere. A game whose music doesn't fit stops preloading at the
            // cap instead of running the heap out, and the LRU carries the remainder. "Lazy" is no
            // longer a mode anyone selects; it is what exceeding the cap looks like.
            var isSfxBySize = bytes.Length <= _shortCompressedBytes;
            if (isSfxBySize || MusicDecodedBytes() < _musicBudgetBytes)
            {
                EnsureDecoded(entry);
            }
            else
            {
                entry.IsMusic = true;
                entry.Permanent = false;
            }
        }

        Console.WriteLine($" Done!");
    }

    public static void Dispose()
    {
        if (!_inited)
        {
            return;
        }

        /*
         * deallocate all the buffers
         * and currently playing sources here
         */
        foreach (var source in _audioSources)
        {
            AL.DeleteSource(source.Source);
            CheckALError();
        }
        _audioSources.Clear();
        foreach (var clip in _audioClips.Values)
        {
            if (clip.Clip != 0)
            {
                AL.DeleteBuffer(clip.Clip);
                CheckALError();
            }
        }
        _audioClips.Clear();
        _musicLru.Clear();
        _lastEventType = null;
        _lastEventId = null;
        _lastEventAt = 0;

        ALC.MakeContextCurrent(ALContext.Null);
        CheckALCError();
        ALC.DestroyContext(_context);
        CheckALCError();
        _context = default;
        ALC.CloseDevice(_device);
        CheckALCError();
        _device = default;

        _inited = false;
    }

    public static void Update()
    {
        UpdateListener(Listener);

        /*
         * we should have a pool of clips
         * when playing, add to the pool. and when its done, remove it and deletesource
         * alternatively, maybe reuse sources? dont think thats even needed and we can gensource each time
         * i guess its not a pool at that point. more of an "active sources" thing.
         * could do the same for buffers if theyre not all made on init
         */
        for (var i = _audioSources.Count - 1; i >= 0; i--)
        {
            var source = _audioSources[i];
            var state = (ALSourceState)AL.GetSource(source.Source, ALGetSourcei.SourceState);
            CheckALError();
            // you cant re-play an existing instance, so stopped audio is used as a sign to clean up
            if (state == ALSourceState.Stopped)
            {
                //DebugLog.Log($"source {source.Source} {source.Asset.Name} stopped manually or done playing");
                AL.DeleteSource(source.Source);
                CheckALError();
                if (source.Asset is not null)
                {
                    UnpinAsset(source.Asset);
                }
                _audioSources.RemoveAt(i);
                continue;
            }

            if (source.GainLerpStartGain is double startGain)
            {
                // This runs once per game step (see CustomWindow.cs's call site), so a fixed
                // per-step slice of the requested duration is as accurate a clock as any --
                // real GM's own sound_fade/audio_sound_gain are similarly frame-driven.
                source.GainLerpElapsedMs += 1000.0 / Entry.GameSpeed;
                var (gain, done) = StepGainLerp(startGain, source.GainLerpTargetGain, source.GainLerpElapsedMs, source.GainLerpTotalMs);

                AL.Source(source.Source, ALSourcef.Gain, (float)gain);
                CheckALError();

                if (done)
                {
                    source.GainLerpStartGain = null;
                }
            }
        }

        AdvancePlayQueues();
    }

    // -- audio_*_play_queue (SoundFunctions.cs) --------------------------------------------------

    private class PlayQueue
    {
        public int Bus;
        public Queue<(int SoundIndex, double Offset)> Pending = new();
        public int? CurrentInstanceId;
    }

    private static readonly Dictionary<int, PlayQueue> _playQueues = new();
    private static int _nextPlayQueue = 1;

    public static int CreatePlayQueue(int bus)
    {
        var id = _nextPlayQueue++;
        _playQueues[id] = new PlayQueue { Bus = bus };
        return id;
    }

    public static void FreePlayQueue(int queue)
    {
        if (_playQueues.Remove(queue, out var q) && q.CurrentInstanceId is { } instanceId)
        {
            var instance = GetAudioInstance(instanceId);
            if (instance != null)
            {
                AL.SourceStop(instance.Source);
                CheckALError();
            }
        }
    }

    public static bool QueueSound(int queue, int soundIndex, double offset)
    {
        if (!_playQueues.TryGetValue(queue, out var q))
        {
            return false;
        }

        q.Pending.Enqueue((soundIndex, offset));
        if (q.CurrentInstanceId == null)
        {
            StartNextInQueue(q);
        }
        return true;
    }

    private static void StartNextInQueue(PlayQueue q)
    {
        if (q.Pending.Count == 0)
        {
            q.CurrentInstanceId = null;
            return;
        }

        var (soundIndex, offset) = q.Pending.Dequeue();
        var asset = GetAudioAsset(soundIndex);
        q.CurrentInstanceId = audio_play_sound(soundIndex, 0, false, asset?.Gain ?? 1, offset, asset?.Pitch ?? 1);
    }

    private static void AdvancePlayQueues()
    {
        foreach (var q in _playQueues.Values)
        {
            if (q.CurrentInstanceId is { } instanceId && GetAudioInstance(instanceId) == null)
            {
                StartNextInQueue(q);
            }
        }
    }

    [Conditional("DEBUG_EXTRA")]
    public static void CheckALCError()
    {
        var e = ALC.GetError(_device);
        if (e != AlcError.NoError)
        {
            throw new Exception($"ALC error: {e}");
        }
    }

    [Conditional("DEBUG_EXTRA")]
    public static void CheckALError()
    {
        var e = AL.GetError();
        if (e != ALError.NoError)
        {
            throw new Exception($"AL error: {e}");
        }
    }


    /// <summary>
    /// Registers a loose audio file for audio_create_stream, without decoding it.
    ///
    /// GameMaker streams these off disk rather than holding them in memory -- that is the whole
    /// difference between audio_create_stream and loading a sound asset -- so decoding one to PCM
    /// here was both wrong and ruinous: a single long track is hundreds of megabytes as float
    /// samples (a 30-minute stereo one is over 600MB), and a game that registers its whole
    /// soundtrack up front asked for all of them at once. In the browser that threw
    /// OutOfMemoryException out of the middle of the game's init script, which then aborted and
    /// left its globals half-built.
    ///
    /// Nothing is read until the sound is actually played, and then <see cref="EnsureDecoded"/>
    /// re-reads it from the file and the music budget can evict it again afterwards.
    /// </summary>
    /// <returns>The new asset index, or -1 if the file isn't there (as GameMaker does).</returns>
    public static int RegisterStreamedFile(string name, string fileName)
    {
        if (!File.Exists(fileName))
        {
            DebugLog.LogWarning($"audio_create_stream: {fileName} not found");
            return -1;
        }

        return RegisterAudioClip(new AudioAsset
        {
            Name = name,
            Clip = 0, // decoded on first play
            Gain = 1,
            Pitch = 1,
            Offset = 0,
            FileName = fileName,
            Streamed = true,
            IsMusic = true,
            Permanent = false,
        });
    }

    public static int RegisterAudioClip(AudioAsset asset)
    {
        var index = AssetIndexManager.Register(AssetType.sounds, asset.Name);

        asset.AssetIndex = index;
        _audioClips.Add(index, asset);
        return index;
    }

    public static void UnregisterAudio(int index)
    {
        if (!_audioClips.ContainsKey(index))
        {
            DebugLog.LogWarning($"UnregisterAudio - couldnt find audio asset {index}");
            return;
        }

        var asset = _audioClips[index];

        // gotta remove sources playing this before we can delete it
        foreach (var source in _audioSources)
        {
            var buffer = AL.GetSource(source.Source, ALGetSourcei.Buffer);
            CheckALError();
            if (asset.Clip == buffer)
            {
                AL.SourceStop(source.Source);
                CheckALError();
            }
        }
        Update(); // hack: deletes the sources. maybe make official stop and delete function

        if (asset.Clip != 0)
        {
            AL.DeleteBuffer(asset.Clip);
            CheckALError();
        }

        _musicLru.Remove(index);
        _audioClips.Remove(index);
        AssetIndexManager.Unregister(AssetType.sounds, asset.Name);
    }

    public static AudioInstance? GetAudioInstance(int instanceId)
    {
        return _audioSources.FirstOrDefault(x => x.SoundInstanceId == instanceId);
    }

    public static AudioInstance[] GetAudioInstances(int assetIndex)
    {
        return _audioSources.Where(x => x.Asset.AssetIndex == assetIndex).ToArray();
    }

    public static AudioInstance[] GetAllAudioInstances()
    {
        return _audioSources.ToArray();
    }

    // These five take whatever index the game passed. Real GameMaker ignores one that names no
    // sound; indexing _audioClips directly instead threw KeyNotFoundException, and a throw out of
    // a builtin aborts the entire calling event (VMExecutor), so one stray audio_sound_pitch(-1)
    // took out everything after it in that event. Seen for real: a game whose init script failed
    // earlier went on to pass the -1 it got back from the call that failed. GetAudioAsset below
    // already looks assets up this way.

    public static void SetAssetGain(int assetIndex, double gain)
    {
        if (_audioClips.TryGetValue(assetIndex, out var asset))
        {
            asset.Gain = gain;
        }
    }

    public static void SetAssetPitch(int assetIndex, double pitch)
    {
        if (_audioClips.TryGetValue(assetIndex, out var asset))
        {
            asset.Pitch = pitch;
        }
    }

    public static double GetAssetPitch(int assetIndex)
    {
        return _audioClips.TryGetValue(assetIndex, out var asset) ? asset.Pitch : 1;
    }

    public static void SetAssetOffset(int assetIndex, double time)
    {
        if (_audioClips.TryGetValue(assetIndex, out var asset))
        {
            asset.Offset = time;
        }
    }

    public static double GetAssetOffset(int assetIndex)
    {
        return _audioClips.TryGetValue(assetIndex, out var asset) ? asset.Offset : 0;
    }

    public static AudioAsset? GetAudioAsset(int assetIndex)
    {
        if (!_audioClips.TryGetValue(assetIndex, out var asset))
        {
            return null;
        }

        return asset;
    }

    public static void StopAllAudio()
    {
        foreach (var item in _audioSources)
        {
            AL.SourceStop(item.Source);
            CheckALError();
        }
        Update(); // hack: deletes the sources. maybe make official stop and delete function
    }

    private static int _highestSoundInstanceId = GMConstants.BASE_SOUND_INDEX;

    private static AudioInstance GetNewAudioInstance()
    {
        if (_audioSources.Count == AudioChannelNum)
        {
            var stolen = _audioSources.MinBy(x => x.Priority)!;
            if (stolen.Asset is not null)
            {
                UnpinAsset(stolen.Asset);
                stolen.Asset = null!;
            }
            return stolen;
        }

        var source = AL.GenSource();
        var instance = new AudioInstance
        {
            SoundInstanceId = ++_highestSoundInstanceId,
            Source = source
        };

        _audioSources.Add(instance);
        return instance;
    }

    /// <summary>
    /// Wires up whatever sound_effect_* chain (SoundFunctionsLegacy.cs) is currently configured for
    /// this sound asset onto the given source, right before it starts playing. Wasm-only -- native
    /// has no DSP effect graph to hang a chain off of (see AudioManager.SetSourcePan's own doc
    /// comment for the same native/wasm split reasoning).
    /// </summary>
    private static void ApplyEffectsIfAny(int source, int index)
    {
#if BROWSER
        AL.ApplySoundEffects(source, index);
#endif
    }

    public static int audio_play_sound(int index, double priority, bool loop, double gain, double offset, double pitch)
    {
        if (index == -1 || !_audioClips.ContainsKey(index))
        {
            // Logged and then indexed anyway, which threw KeyNotFoundException and took down the
            // whole calling event. GameMaker just doesn't play anything and hands back -1.
            DebugLog.LogError($"AudioDatabase doesn't contain {index}!");
            return -1;
        }

        var asset = _audioClips[index];
        var clip = EnsureDecoded(asset);
        PinAsset(asset);

        var instance = GetNewAudioInstance();
        var source = instance.Source;

        AL.Source(source, ALSourcei.Buffer, clip);
        CheckALError();
        AL.Source(source, ALSourceb.Looping, loop);
        CheckALError();
        AL.Source(source, ALSourcef.Gain, (float)gain);
        CheckALError();
        AL.Source(source, ALSourcef.SecOffset, (float)offset);
        CheckALError();
        AL.Source(source, ALSourcef.Pitch, (float)pitch);
        CheckALError();
        ApplyEffectsIfAny(source, index);
        AL.SourcePlay(source);
        CheckALError();

        instance.Asset = asset;
        instance.Priority = priority;

        return instance.SoundInstanceId;
    }

    public static int PlaySoundOnEmitter(AudioEmitter emitter, int index, double priority, bool loop, double gain, double offset, double pitch, int listener_mask)
    {
        if (index == -1 || !_audioClips.ContainsKey(index))
        {
            // Logged and then indexed anyway, which threw KeyNotFoundException and took down the
            // whole calling event. GameMaker just doesn't play anything and hands back -1.
            DebugLog.LogError($"AudioDatabase doesn't contain {index}!");
            return -1;
        }

        var asset = _audioClips[index];
        var clip = EnsureDecoded(asset);
        PinAsset(asset);

        var instance = GetNewAudioInstance();
        var source = instance.Source;

        AL.Source(source, ALSourcei.Buffer, clip);
        CheckALError();
        AL.Source(source, ALSourceb.Looping, loop);
        CheckALError();
        AL.Source(source, ALSourcef.Gain, (float)gain);
        CheckALError();
        AL.Source(source, ALSourcef.SecOffset, (float)offset);
        CheckALError();
        AL.Source(source, ALSourcef.Pitch, (float)pitch);
        CheckALError();

        AL.Source(source, ALSourcef.MaxDistance, emitter.FalloffMax);
        CheckALError();
        AL.Source(source, ALSourcef.ReferenceDistance, emitter.FalloffRef);
        CheckALError();
        AL.Source(source, ALSourcef.RolloffFactor, emitter.FalloffFac);
        CheckALError();
        AL.Source(source, ALSource3f.Position, emitter.Position.X, emitter.Position.Y, emitter.Position.Z);
        CheckALError();
        AL.Source(source, ALSource3f.Velocity, emitter.Velocity.X, emitter.Velocity.Y, emitter.Velocity.Z);
        CheckALError();

        ApplyEffectsIfAny(source, index);
        AL.SourcePlay(source);
        CheckALError();

        instance.Asset = asset;
        instance.Priority = priority;

        emitter.AttachedSounds.Add(instance);

        return instance.SoundInstanceId;
    }

    /// <summary>
    /// GM8's legacy sound_pan (SoundFunctionsLegacy.cs) is engine-level, not a real OpenAL concept
    /// (no standard AL_PAN token) -- real 2D panning would need actual 3D source positioning wired
    /// up against a configured distance model, which isn't set up here on native (out of scope, and
    /// no worse than before this file gained real audio_*/sound_* support). Only wasm has a real,
    /// direct implementation, via Web Audio's own StereoPannerNode (OpenTKShim/AL.cs, webaudio.js).
    /// </summary>
    public static void SetSourcePan(int source, float pan)
    {
#if BROWSER
        AL.SourceSetPan(source, pan);
#else
        DebugLog.LogWarning("sound_pan is not implemented on the native build.");
#endif
    }

    /// <summary>
    /// sound_3d_set_sound_cone (SoundFunctionsLegacy.cs) -- like SetSourcePan, only wasm has a real
    /// implementation (Web Audio's own PannerNode cone properties, see webaudio.js); native has no
    /// distance-model/falloff graph wired up to hang a cone off of.
    /// </summary>
    public static void SetSourceCone(int source, float dirX, float dirY, float dirZ, float angleIn, float angleOut, float volumeOut)
    {
#if BROWSER
        AL.SourceSetOrientation(source, dirX, dirY, dirZ);
        AL.SourceSetCone(source, angleIn, angleOut, volumeOut);
#else
        DebugLog.LogWarning("sound_3d_set_sound_cone is not implemented on the native build.");
#endif
    }

    // -- legacy sound_effect_* (SoundFunctionsLegacy.cs) -----------------------------------------
    //
    // Wasm-only real implementation (see webaudio.js's own doc comment on this section for the
    // Web-Audio-approximation-of-DirectSound reasoning); native has no DSP effect graph to hang a
    // chain off of, same native/wasm split as SetSourcePan/SetSourceCone above.

    public static void SetSoundEffectMask(int assetIndex, int mask)
    {
#if BROWSER
        AL.EffectSetMask(assetIndex, mask);
#else
        DebugLog.LogWarning("sound_effect_set is not implemented on the native build.");
#endif
    }

    public static void SetSoundEffectChorus(int assetIndex, float wetDryMix, float depth, float feedback, float frequency, int waveform)
    {
#if BROWSER
        AL.EffectSetChorus(assetIndex, wetDryMix, depth, feedback, frequency, waveform);
#endif
    }

    public static void SetSoundEffectFlanger(int assetIndex, float wetDryMix, float depth, float feedback, float frequency, int waveform)
    {
#if BROWSER
        AL.EffectSetFlanger(assetIndex, wetDryMix, depth, feedback, frequency, waveform);
#endif
    }

    public static void SetSoundEffectEcho(int assetIndex, float wetDryMix, float feedback, float leftDelay, float rightDelay)
    {
#if BROWSER
        AL.EffectSetEcho(assetIndex, wetDryMix, feedback, leftDelay, rightDelay);
#endif
    }

    public static void SetSoundEffectCompressor(int assetIndex, float gain, float attack, float release, float threshold, float ratio)
    {
#if BROWSER
        AL.EffectSetCompressor(assetIndex, gain, attack, release, threshold, ratio);
#endif
    }

    public static void SetSoundEffectEqualizer(int assetIndex, float center, float bandwidth, float gain)
    {
#if BROWSER
        AL.EffectSetEqualizer(assetIndex, center, bandwidth, gain);
#endif
    }

    public static void SetSoundEffectGargle(int assetIndex, float rate, int waveShape)
    {
#if BROWSER
        AL.EffectSetGargle(assetIndex, rate, waveShape);
#endif
    }

    public static void SetSoundEffectReverb(int assetIndex, float gain, float mix, float time, float ratio)
    {
#if BROWSER
        AL.EffectSetReverb(assetIndex, gain, mix, time, ratio);
#endif
    }

    public static void ChangeGain(AudioInstance instance, double volume, double milliseconds)
    {
        volume = Math.Max(0, volume); // TODO : check if this is what GM does?

        if (milliseconds <= 0)
        {
            instance.GainLerpStartGain = null;
            AL.Source(instance.Source, ALSourcef.Gain, (float)volume);
            CheckALError();
            return;
        }

        // Stepped once per frame in Update() below, not here -- start from whatever the source's
        // gain actually is right now (which may itself be mid-fade) so a new fade always begins
        // from the audible volume, not wherever an earlier fade's target was.
        AL.GetSource(instance.Source, ALSourcef.Gain, out var currentGain);
        CheckALError();

        instance.GainLerpStartGain = currentGain;
        instance.GainLerpTargetGain = volume;
        instance.GainLerpTotalMs = milliseconds;
        instance.GainLerpElapsedMs = 0;
    }

    /// <summary>
    /// Pure interpolation step for a gain fade -- pulled out of Update() so it's testable without
    /// a real OpenAL context. Returns the gain to apply this step and whether the fade has finished.
    /// </summary>
    public static (double gain, bool done) StepGainLerp(double startGain, double targetGain, double elapsedMs, double totalMs)
    {
        var t = Math.Clamp(elapsedMs / totalMs, 0.0, 1.0);
        var gain = startGain + ((targetGain - startGain) * t);
        return (gain, t >= 1.0);
    }

    public static double GetClipLength(AudioAsset asset)
    {
        // TODO : should this account for offset? speed? idk!!!

        if (asset.DurationSecs > 0 && asset.Clip == 0)
        {
            return asset.DurationSecs;
        }

        EnsureDecoded(asset);
        if (asset.DurationSecs > 0)
        {
            return asset.DurationSecs;
        }

        var buffer = asset.Clip;
        AL.GetBuffer(buffer, ALGetBufferi.Size, out var sizeInBytes);
        CheckALError();
        AL.GetBuffer(buffer, ALGetBufferi.Channels, out var channelCount);
        CheckALError();
        AL.GetBuffer(buffer, ALGetBufferi.Bits, out var bitDepth);
        CheckALError();
        AL.GetBuffer(buffer, ALGetBufferi.Frequency, out var frequency);
        CheckALError();

        var numberOfBits = sizeInBytes * 8;
        var sampleLength = numberOfBits / (channelCount * bitDepth);
        return sampleLength / (double)frequency;
    }

    public static IEnumerable<string> GetRecordingDeviceNames()
    {
        var list = ALC.GetStringList(GetEnumerationStringList.CaptureDeviceSpecifier);
        CheckALCError();

        const string OPEN_AL_IS_DUMB_AND_SMELLY = "OpenAL Soft on ";
        return list.Select(x => x.StartsWith(OPEN_AL_IS_DUMB_AND_SMELLY)
            ? x[OPEN_AL_IS_DUMB_AND_SMELLY.Length..]
            : x);
    }

    public static int AudioEmitterCreate()
    {
        var inactiveIndex = _audioEmitters.FindIndex(x => x.Active == false);

        if (inactiveIndex != -1)
        {
            _audioEmitters[inactiveIndex].Reset();
            return inactiveIndex;
        }

        var index = _audioEmitters.Count;
        var emitter = new AudioEmitter { ID = index };
        _audioEmitters.Add(emitter);
        return index;
    }

    public static AudioEmitter GetAudioEmitter(int index)
    {
        return _audioEmitters[index];
    }

    public static bool AudioEmitterExists(int index)
    {
        return _audioEmitters.Count > index;
    }

    public static void UpdateListener(AudioListener listener)
    {
        AL.Listener(ALListener3f.Position, listener.Position.X, listener.Position.Y, listener.Position.Z);
        AL.Listener(ALListener3f.Velocity, listener.Velocity.X, listener.Velocity.Y, listener.Velocity.Z);
        AL.Listener(ALListenerfv.Orientation, ref listener.At, ref listener.Up);
    }
}
