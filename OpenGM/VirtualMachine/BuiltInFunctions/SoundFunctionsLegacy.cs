using OpenGM.IO;
using OpenTK.Audio.OpenAL;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// GM8-era legacy sound_* functions, layered on top of the same AudioManager/AL surface the modern
/// audio_* functions (SoundFunctions.cs) already use (backed by AL.cs/webaudio.js in the browser), on
/// both platforms. Positional/3D audio (sound_3d_*) and DirectSound effect chains
/// (sound_effect_*) are not implemented -- no PannerNode graph or effect nodes are wired
/// up (see AL.cs's own doc comment) -- so those are left unregistered (falling back to
/// ExtensionManager/ScriptResolver's normal stub-and-log behavior for any real game that calls
/// them) rather than silently pretending to work.
/// </summary>
public static class SoundFunctionsLegacy
{
    private static string? _searchDirectory;

    private static AudioInstance? FirstInstanceOf(int assetIndex) => AudioManager.GetAudioInstances(assetIndex).FirstOrDefault();

    [GMLFunction("sound_play")]
    public static object? sound_play(object?[] args)
    {
        var index = args[0].Conv<int>();
        var asset = AudioManager.GetAudioAsset(index);
        if (asset == null)
        {
            DebugLog.LogWarning($"sound_play - tried to play nonexistent sound {index}");
            return null;
        }

        AudioManager.audio_play_sound(index, 0, false, asset.Gain, asset.Offset, asset.Pitch);
        return null;
    }

    [GMLFunction("sound_loop")]
    public static object? sound_loop(object?[] args)
    {
        var index = args[0].Conv<int>();
        var asset = AudioManager.GetAudioAsset(index);
        if (asset == null)
        {
            DebugLog.LogWarning($"sound_loop - tried to loop nonexistent sound {index}");
            return null;
        }

        AudioManager.audio_play_sound(index, 0, true, asset.Gain, asset.Offset, asset.Pitch);
        return null;
    }

    [GMLFunction("sound_stop")]
    public static object? sound_stop(object?[] args)
    {
        var index = args[0].Conv<int>();
        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.SourceStop(instance.Source);
        }
        AudioManager.Update(); // reclaims the now-stopped sources
        return null;
    }

    [GMLFunction("sound_stop_all")]
    public static object? sound_stop_all(object?[] args)
    {
        AudioManager.StopAllAudio();
        return 0; // "returns nothing" is 0 in GameMaker, not undefined -- see move_towards_point
    }

    [GMLFunction("sound_isplaying")]
    public static object? sound_isplaying(object?[] args)
    {
        var index = args[0].Conv<int>();
        return AudioManager.GetAudioInstances(index).Length > 0;
    }

    [GMLFunction("sound_exists")]
    public static object? sound_exists(object?[] args)
    {
        var index = args[0].Conv<int>();
        return AudioManager.GetAudioAsset(index) != null;
    }

    [GMLFunction("sound_get_name")]
    [GMLFunction("sound_name")]
    public static object? sound_get_name(object?[] args)
    {
        var index = args[0].Conv<int>();
        return AudioManager.GetAudioAsset(index)?.Name ?? "<undefined>";
    }

    // Every sound is decoded fully and kept resident once loaded (see AudioManager.LoadSounds) --
    // there's no separate "not yet loaded into memory" state to report here.
    [GMLFunction("sound_get_preload")]
    public static object? sound_get_preload(object?[] args) => true;

    // GM8's snd_normal/snd_background/snd_3d/snd_mmplayer kinds aren't tracked as separate asset
    // metadata here (every sound behaves the same way once decoded to a Web Audio/OpenAL buffer);
    // 0 (snd_normal) is the overwhelmingly common case real games actually check for.
    [GMLFunction("sound_get_kind")]
    public static object? sound_get_kind(object?[] args) => 0;

    [GMLFunction("sound_volume")]
    public static object? sound_volume(object?[] args)
    {
        var index = args[0].Conv<int>();
        var volume = args[1].Conv<double>();

        AudioManager.SetAssetGain(index, volume);
        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.Source(instance.Source, ALSourcef.Gain, (float)volume);
        }
        return null;
    }

    [GMLFunction("sound_fade")]
    public static object? sound_fade(object?[] args)
    {
        var index = args[0].Conv<int>();
        var volume = args[1].Conv<double>();
        var time = args.Length > 2 ? args[2].Conv<double>() : 0;

        AudioManager.SetAssetGain(index, volume);
        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AudioManager.ChangeGain(instance, volume, time);
        }
        return null;
    }

    [GMLFunction("sound_pan")]
    public static object? sound_pan(object?[] args)
    {
        var index = args[0].Conv<int>();
        var pan = Math.Clamp(args[1].Conv<float>(), -1f, 1f);

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AudioManager.SetSourcePan(instance.Source, pan);
        }
        return null;
    }

    // Real DirectSound global volume on GM8 only affects sounds played after it's set; this engine
    // applies it immediately and globally via the listener gain instead (matches audio_set_master_gain).
    [GMLFunction("sound_global_volume")]
    public static object? sound_global_volume(object?[] args)
    {
        var volume = Math.Max(0, args[0].Conv<double>());
        AL.Listener(ALListenerf.Gain, (float)volume);
        return null;
    }

    [GMLFunction("sound_set_search_directory")]
    public static object? sound_set_search_directory(object?[] args)
    {
        var dir = args[0].Conv<string>();
        _searchDirectory = string.IsNullOrEmpty(dir) ? null : dir;
        return null;
    }

    private static string ResolveSoundPath(string fname)
    {
        // Entry.ResolveDataWinPath already leaves an absolute path (e.g. one a game built as
        // "working_directory + fname") untouched instead of double-prefixing it, so only apply
        // sound_set_search_directory's own prefix when fname isn't already one of those.
        if (_searchDirectory != null && !Path.IsPathRooted(fname.Replace('\\', '/')))
        {
            fname = Path.Combine(_searchDirectory, fname);
        }
        return Entry.ResolveDataWinPath(fname);
    }

    // kind/preload are accepted for signature compatibility but unused -- see sound_get_kind/
    // sound_get_preload's own doc comments for why neither is tracked as real per-asset state here.
    [GMLFunction("sound_add")]
    public static object? sound_add(object?[] args)
    {
        var fname = args[0].Conv<string>();

        var path = ResolveSoundPath(fname);
        if (!File.Exists(path))
        {
            DebugLog.LogWarning($"sound_add - file not found: {path}");
            return -1;
        }

        var (data, stereo, freq) = AudioManager.DecodeSoundBytes(File.ReadAllBytes(path), fname);

        var buffer = AL.GenBuffer();
        AL.BufferData(buffer, stereo ? ALFormat.StereoFloat32Ext : ALFormat.MonoFloat32Ext, data, freq);

        return AudioManager.RegisterAudioClip(new AudioAsset
        {
            Name = Path.GetFileNameWithoutExtension(fname),
            Clip = buffer,
            Gain = 1,
            Pitch = 1,
            Offset = 0,
        });
    }

    [GMLFunction("sound_replace")]
    public static object? sound_replace(object?[] args)
    {
        var index = args[0].Conv<int>();
        var fname = args[1].Conv<string>();

        var existing = AudioManager.GetAudioAsset(index);
        if (existing == null)
        {
            DebugLog.LogWarning($"sound_replace - tried to replace nonexistent sound {index}");
            return null;
        }

        var path = ResolveSoundPath(fname);
        if (!File.Exists(path))
        {
            DebugLog.LogWarning($"sound_replace - file not found: {path}");
            return null;
        }

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.SourceStop(instance.Source);
        }
        AudioManager.Update();

        var (data, stereo, freq) = AudioManager.DecodeSoundBytes(File.ReadAllBytes(path), fname);

        AL.DeleteBuffer(existing.Clip);
        var buffer = AL.GenBuffer();
        AL.BufferData(buffer, stereo ? ALFormat.StereoFloat32Ext : ALFormat.MonoFloat32Ext, data, freq);
        existing.Clip = buffer;

        return null;
    }

    [GMLFunction("sound_delete")]
    public static object? sound_delete(object?[] args)
    {
        var index = args[0].Conv<int>();
        AudioManager.UnregisterAudio(index);
        return null;
    }

    // Real memory-reclaim semantics (discard frees the decoded buffer but keeps the asset slot
    // valid; restore reloads it) aren't implemented -- every sound stays fully resident once loaded
    // (see sound_get_preload's own doc comment), so discard/restore are safe no-ops rather than
    // actually freeing anything real games could then fail to find again.
    [GMLFunction("sound_discard", GMLFunctionFlags.Stub)]
    public static object? sound_discard(object?[] args) => null;

    [GMLFunction("sound_restore", GMLFunctionFlags.Stub)]
    public static object? sound_restore(object?[] args) => null;

    // MIDI-sequence tempo -- this engine has no MIDI/sequence playback backend at all (WAV/OGG
    // only), so there's nothing for this to meaningfully act on.
    [GMLFunction("sound_background_tempo", GMLFunctionFlags.Stub)]
    public static object? sound_background_tempo(object?[] args) => null;

    // -- sound_3d_* -------------------------------------------------------------------------
    //
    // Legacy DirectSound-era 3D positional audio, applied per sound *asset* index (matching
    // sound_volume/sound_pan's own index-based, not instance-based, convention above) -- every
    // currently-playing instance of that asset gets repositioned. Position/distance are real,
    // standard OpenAL members (ALSource3f.Position, ALSourcef.Reference/MaxDistance), so they work
    // on native too; velocity has no real effect anywhere (see AL.cs's own doc comment -- no
    // Doppler support left in the Web Audio spec, and this engine's native OpenAL path never wired
    // up AL_DOPPLER_FACTOR either), and the cone functions are wasm-only (see
    // AudioManager.SetSourceCone's own doc comment).

    [GMLFunction("sound_3d_set_sound_position")]
    public static object? sound_3d_set_sound_position(object?[] args)
    {
        var index = args[0].Conv<int>();
        var x = args[1].Conv<float>();
        var y = args[2].Conv<float>();
        var z = args[3].Conv<float>();

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.Source(instance.Source, ALSource3f.Position, x, y, z);
        }
        return null;
    }

    [GMLFunction("sound_3d_set_sound_velocity")]
    public static object? sound_3d_set_sound_velocity(object?[] args)
    {
        var index = args[0].Conv<int>();
        var vx = args[1].Conv<float>();
        var vy = args[2].Conv<float>();
        var vz = args[3].Conv<float>();

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.Source(instance.Source, ALSource3f.Velocity, vx, vy, vz);
        }
        return null;
    }

    [GMLFunction("sound_3d_set_sound_distance")]
    public static object? sound_3d_set_sound_distance(object?[] args)
    {
        var index = args[0].Conv<int>();
        var minDist = args[1].Conv<float>();
        var maxDist = args[2].Conv<float>();

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AL.Source(instance.Source, ALSourcef.ReferenceDistance, minDist);
            AL.Source(instance.Source, ALSourcef.MaxDistance, maxDist);
        }
        return null;
    }

    [GMLFunction("sound_3d_set_sound_cone")]
    public static object? sound_3d_set_sound_cone(object?[] args)
    {
        var index = args[0].Conv<int>();
        var x = args[1].Conv<float>();
        var y = args[2].Conv<float>();
        var z = args[3].Conv<float>();
        var angleIn = args[4].Conv<float>();
        var angleOut = args[5].Conv<float>();
        var volOut = args[6].Conv<float>();

        foreach (var instance in AudioManager.GetAudioInstances(index))
        {
            AudioManager.SetSourceCone(instance.Source, x, y, z, angleIn, angleOut, volOut);
        }
        return null;
    }

    // -- sound_effect_* (legacy DirectSound effect chains) -----------------------------------
    //
    // Every effect-parameter setter takes the sound *asset* index as its own first argument
    // (consistent with sound_volume/sound_pan/sound_3d_* above), storing that effect's parameters
    // for whichever sound index it's called on -- sound_effect_set(index, effect) then combines any
    // of the se_* bitmask constants to actually enable them for that index's next-played instance.
    // See AudioManager.SetSoundEffectMask's own doc comment for the wasm-only/native-stub split,
    // and webaudio.js's for how each effect is approximated in Web Audio.

    [GMLFunction("sound_effect_set")]
    public static object? sound_effect_set(object?[] args)
    {
        var index = args[0].Conv<int>();
        var effect = args[1].Conv<int>();
        AudioManager.SetSoundEffectMask(index, effect);
        return null;
    }

    [GMLFunction("sound_effect_chorus")]
    public static object? sound_effect_chorus(object?[] args)
    {
        var index = args[0].Conv<int>();
        var wetDryMix = args[1].Conv<float>();
        var depth = args[2].Conv<float>();
        var feedback = args[3].Conv<float>();
        var frequency = args[4].Conv<float>();
        var waveform = args[5].Conv<int>();
        AudioManager.SetSoundEffectChorus(index, wetDryMix, depth, feedback, frequency, waveform);
        return null;
    }

    [GMLFunction("sound_effect_flanger")]
    public static object? sound_effect_flanger(object?[] args)
    {
        var index = args[0].Conv<int>();
        var wetDryMix = args[1].Conv<float>();
        var depth = args[2].Conv<float>();
        var feedback = args[3].Conv<float>();
        var frequency = args[4].Conv<float>();
        var waveform = args[5].Conv<int>();
        AudioManager.SetSoundEffectFlanger(index, wetDryMix, depth, feedback, frequency, waveform);
        return null;
    }

    [GMLFunction("sound_effect_echo")]
    public static object? sound_effect_echo(object?[] args)
    {
        var index = args[0].Conv<int>();
        var wetDryMix = args[1].Conv<float>();
        var feedback = args[2].Conv<float>();
        var leftDelay = args[3].Conv<float>();
        var rightDelay = args[4].Conv<float>();
        AudioManager.SetSoundEffectEcho(index, wetDryMix, feedback, leftDelay, rightDelay);
        return null;
    }

    [GMLFunction("sound_effect_compressor")]
    public static object? sound_effect_compressor(object?[] args)
    {
        var index = args[0].Conv<int>();
        var gain = args[1].Conv<float>();
        var attack = args[2].Conv<float>();
        var release = args[3].Conv<float>();
        var threshold = args[4].Conv<float>();
        var ratio = args[5].Conv<float>();
        AudioManager.SetSoundEffectCompressor(index, gain, attack, release, threshold, ratio);
        return null;
    }

    [GMLFunction("sound_effect_equalizer")]
    public static object? sound_effect_equalizer(object?[] args)
    {
        var index = args[0].Conv<int>();
        var center = args[1].Conv<float>();
        var bandwidth = args[2].Conv<float>();
        var gain = args[3].Conv<float>();
        AudioManager.SetSoundEffectEqualizer(index, center, bandwidth, gain);
        return null;
    }

    [GMLFunction("sound_effect_gargle")]
    public static object? sound_effect_gargle(object?[] args)
    {
        var index = args[0].Conv<int>();
        var rate = args[1].Conv<float>();
        var waveShape = args[2].Conv<int>();
        AudioManager.SetSoundEffectGargle(index, rate, waveShape);
        return null;
    }

    [GMLFunction("sound_effect_reverb")]
    public static object? sound_effect_reverb(object?[] args)
    {
        var index = args[0].Conv<int>();
        var gain = args[1].Conv<float>();
        var mix = args[2].Conv<float>();
        var time = args[3].Conv<float>();
        var ratio = args[4].Conv<float>();
        AudioManager.SetSoundEffectReverb(index, gain, mix, time, ratio);
        return null;
    }
}
