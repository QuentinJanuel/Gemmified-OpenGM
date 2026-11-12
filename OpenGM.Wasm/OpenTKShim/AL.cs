using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace OpenTK.Audio.OpenAL;

/// <summary>
/// Namespace-shadowed replacement for the real OpenTK.Audio.OpenAL AL/ALC static classes (a
/// P/Invoke binding to a native OpenAL driver with no wasm equivalent) -- same "compile against a
/// same-named stand-in" trick OpenTKShim/GL.cs already uses for the renderer. Because
/// AudioManager.cs and SoundFunctions.cs reference "AL.Foo(...)"/"ALC.Foo(...)" purely by name, and
/// this project doesn't reference the real OpenTK.Audio package, both files bind to this shim
/// automatically once un-excluded from the build -- zero edits needed to either file. Backed by
/// JSImport calls into webaudio.js, which wraps a real Web Audio API AudioContext, using the same
/// architecture (and the same GainNode/AudioBufferSourceNode-per-play, manual position-tracking
/// pause/resume) as OpenGMK's own wasm build (gm8emulator/src/game/audio_wasm.rs) -- Web Audio has
/// no native pause/seek for a source node once started, so both ports solve it the same way: stop
/// the current node, remember an elapsed offset, and start a fresh node from that offset on resume.
///
/// Real 3D spatialization (source/listener position, falloff/distance model) is backed by a real
/// Web Audio PannerNode per source (webaudio.js) -- velocity/Doppler has no native equivalent in
/// the current Web Audio spec (removed from PannerNode/AudioListener years ago), so that alone is
/// tracked nowhere. Capture/recording (ALC.CaptureOpenDevice et al) is still out of scope --
/// GetStringList for capture devices always reports zero devices, which keeps SoundFunctions.cs's
/// own recorder-count/list functions safely returning "none available" without ever reaching a
/// real capture call.
/// </summary>
public static partial class AL
{
    private const string Module = "webaudio";

    [JSImport("genSource", Module)]
    public static partial int GenSource();

    [JSImport("genBuffer", Module)]
    public static partial int GenBuffer();

    [JSImport("deleteSource", Module)]
    public static partial void DeleteSource(int source);

    [JSImport("deleteBuffer", Module)]
    public static partial void DeleteBuffer(int buffer);

    [JSImport("bufferData", Module)]
    private static partial void BufferDataInternal(int buffer, int channels, int freq, byte[] interleavedFloatBytes);

    public static void BufferData(int buffer, ALFormat format, float[] data, int freq)
    {
        var channels = format is ALFormat.StereoFloat32Ext or ALFormat.Stereo8 or ALFormat.Stereo16 ? 2 : 1;
        var bytes = MemoryMarshal.AsBytes<float>(data).ToArray();
        BufferDataInternal(buffer, channels, freq, bytes);
    }

    [JSImport("sourceSetBufferi", Module)]
    private static partial void SourceSetBufferInternal(int source, int buffer);

    public static void Source(int source, ALSourcei param, int value) => SourceSetBufferInternal(source, value);

    [JSImport("sourceSetLooping", Module)]
    private static partial void SourceSetLoopingInternal(int source, bool looping);

    public static void Source(int source, ALSourceb param, bool value) => SourceSetLoopingInternal(source, value);

    [JSImport("sourceSetGain", Module)]
    private static partial void SourceSetGainInternal(int source, double value);
    [JSImport("sourceSetPitch", Module)]
    private static partial void SourceSetPitchInternal(int source, double value);
    [JSImport("sourceSetOffset", Module)]
    private static partial void SourceSetOffsetInternal(int source, double value);
    // Not a real OpenAL concept (no standard AL_PAN token) -- GM8's legacy sound_pan is engine-level,
    // so this is a pragmatic addition to the shim rather than a real AL member, backed directly by
    // Web Audio's own StereoPannerNode (see webaudio.js).
    [JSImport("sourceSetPan", Module)]
    public static partial void SourceSetPan(int source, double pan);
    [JSImport("sourceGetPan", Module)]
    public static partial double SourceGetPan(int source);
    // MaxDistance/ReferenceDistance/RolloffFactor are tracked JS-side but not applied to any audio
    // node yet (see this file's doc comment) -- still routed through so callers compile unchanged
    // and don't throw on an emitter-backed sound_play.
    [JSImport("sourceSetFalloff", Module)]
    private static partial void SourceSetFalloffInternal(int source, string which, double value);

    public static void Source(int source, ALSourcef param, float value)
    {
        switch (param)
        {
            case ALSourcef.Gain:
                SourceSetGainInternal(source, value);
                break;
            case ALSourcef.Pitch:
                SourceSetPitchInternal(source, value);
                break;
            case ALSourcef.SecOffset:
                SourceSetOffsetInternal(source, value);
                break;
            default:
                SourceSetFalloffInternal(source, param.ToString(), value);
                break;
        }
    }

    [JSImport("sourceSetPosition", Module)]
    private static partial void SourceSetPositionInternal(int source, double x, double y, double z);
    [JSImport("sourceSetVelocity", Module)]
    private static partial void SourceSetVelocityInternal(int source, double x, double y, double z);

    public static void Source(int source, ALSource3f param, float x, float y, float z)
    {
        if (param == ALSource3f.Position)
            SourceSetPositionInternal(source, x, y, z);
        else
            SourceSetVelocityInternal(source, x, y, z);
    }

    // Not real OpenAL members (no standard AL_CONE_*/AL_ORIENTATION source tokens in desktop
    // OpenAL's actual API either) -- pragmatic additions backed directly by Web Audio's own
    // PannerNode cone/orientation properties, for sound_3d_set_sound_cone (SoundFunctionsLegacy.cs).
    [JSImport("sourceSetCone", Module)]
    public static partial void SourceSetCone(int source, double innerAngle, double outerAngle, double outerGain);
    [JSImport("sourceSetOrientation", Module)]
    public static partial void SourceSetOrientation(int source, double x, double y, double z);

    [JSImport("sourcePlay", Module)]
    public static partial void SourcePlay(int source);

    [JSImport("sourceStop", Module)]
    public static partial void SourceStop(int source);

    [JSImport("sourcePause", Module)]
    public static partial void SourcePause(int source);

    [JSImport("sourceGetState", Module)]
    private static partial int SourceGetStateInternal(int source);
    [JSImport("sourceGetBufferi", Module)]
    private static partial int SourceGetBufferInternal(int source);

    public static int GetSource(int source, ALGetSourcei param) =>
        param == ALGetSourcei.Buffer ? SourceGetBufferInternal(source) : SourceGetStateInternal(source);

    [JSImport("sourceGetGain", Module)]
    private static partial double SourceGetGainInternal(int source);
    [JSImport("sourceGetPitch", Module)]
    private static partial double SourceGetPitchInternal(int source);
    [JSImport("sourceGetOffset", Module)]
    private static partial double SourceGetOffsetInternal(int source);

    private static float GetSourcefInternal(int source, ALSourcef param) => param switch
    {
        ALSourcef.Gain => (float)SourceGetGainInternal(source),
        ALSourcef.Pitch => (float)SourceGetPitchInternal(source),
        ALSourcef.SecOffset => (float)SourceGetOffsetInternal(source),
        _ => 0f,
    };

    public static float GetSource(int source, ALSourcef param) => GetSourcefInternal(source, param);

    public static void GetSource(int source, ALSourcef param, out float value) => value = GetSourcefInternal(source, param);

    [JSImport("bufferGetChannels", Module)]
    private static partial int BufferGetChannelsInternal(int buffer);
    [JSImport("bufferGetBits", Module)]
    private static partial int BufferGetBitsInternal(int buffer);
    [JSImport("bufferGetFrequency", Module)]
    private static partial int BufferGetFrequencyInternal(int buffer);
    [JSImport("bufferGetSizeBytes", Module)]
    private static partial int BufferGetSizeBytesInternal(int buffer);

    public static void GetBuffer(int buffer, ALGetBufferi param, out int value)
    {
        value = param switch
        {
            ALGetBufferi.Size => BufferGetSizeBytesInternal(buffer),
            ALGetBufferi.Channels => BufferGetChannelsInternal(buffer),
            ALGetBufferi.Bits => BufferGetBitsInternal(buffer),
            ALGetBufferi.Frequency => BufferGetFrequencyInternal(buffer),
            _ => 0,
        };
    }

    [JSImport("listenerSetGain", Module)]
    private static partial void ListenerSetGainInternal(double gain);
    [JSImport("listenerSetPosition", Module)]
    private static partial void ListenerSetPositionInternal(double x, double y, double z);
    [JSImport("listenerSetVelocity", Module)]
    private static partial void ListenerSetVelocityInternal(double x, double y, double z);
    [JSImport("listenerSetOrientation", Module)]
    private static partial void ListenerSetOrientationInternal(double atX, double atY, double atZ, double upX, double upY, double upZ);

    public static void Listener(ALListener3f param, float x, float y, float z)
    {
        if (param == ALListener3f.Position)
            ListenerSetPositionInternal(x, y, z);
        else
            ListenerSetVelocityInternal(x, y, z);
    }

    public static void Listener(ALListenerf param, float value) => ListenerSetGainInternal(value);

    [JSImport("listenerGetGain", Module)]
    private static partial double ListenerGetGainInternal();

    public static void GetListener(ALListenerf param, out float value) => value = (float)ListenerGetGainInternal();

    public static void Listener(ALListenerfv param, ref Vector3 at, ref Vector3 up) =>
        ListenerSetOrientationInternal(at.X, at.Y, at.Z, up.X, up.Y, up.Z);

    public static string Get(ALGetString param) => param switch
    {
        ALGetString.Version => "OpenGM WebAudio Shim 1.0",
        ALGetString.Vendor => "OpenGM",
        _ => "",
    };

    public static ALError GetError() => ALError.NoError;

    [JSImport("setDistanceModel", Module)]
    private static partial void SetDistanceModelInternal(string modelName);

    public static void DistanceModel(ALDistanceModel model) => SetDistanceModelInternal(model.ToString());

    // -- legacy sound_effect_* (see webaudio.js's own doc comment on this section) --------------

    [JSImport("effectSetMask", Module)]
    public static partial void EffectSetMask(int assetIndex, int mask);
    [JSImport("effectSetChorus", Module)]
    public static partial void EffectSetChorus(int assetIndex, double wetDryMix, double depth, double feedback, double frequency, int waveform);
    [JSImport("effectSetFlanger", Module)]
    public static partial void EffectSetFlanger(int assetIndex, double wetDryMix, double depth, double feedback, double frequency, int waveform);
    [JSImport("effectSetEcho", Module)]
    public static partial void EffectSetEcho(int assetIndex, double wetDryMix, double feedback, double leftDelay, double rightDelay);
    [JSImport("effectSetCompressor", Module)]
    public static partial void EffectSetCompressor(int assetIndex, double gain, double attack, double release, double threshold, double ratio);
    [JSImport("effectSetEqualizer", Module)]
    public static partial void EffectSetEqualizer(int assetIndex, double center, double bandwidth, double gain);
    [JSImport("effectSetGargle", Module)]
    public static partial void EffectSetGargle(int assetIndex, double rate, int waveShape);
    [JSImport("effectSetReverb", Module)]
    public static partial void EffectSetReverb(int assetIndex, double gain, double mix, double time, double ratio);
    [JSImport("applySoundEffects", Module)]
    public static partial void ApplySoundEffects(int source, int assetIndex);
}

public static partial class ALC
{
    private const string Module = "webaudio";

    [JSImport("init", Module)]
    private static partial void InitInternal();

    public static ALDevice OpenDevice(string? name)
    {
        InitInternal();
        return new ALDevice { Handle = 1 };
    }

    public static ALContext CreateContext(ALDevice device, ALContextAttributes attributes) => new() { Handle = 1 };

    public static bool MakeContextCurrent(ALContext context) => true;

    public static void DestroyContext(ALContext context)
    {
    }

    public static bool CloseDevice(ALDevice device) => true;

    public static AlcError GetError(ALDevice device) => AlcError.NoError;

    [JSImport("getCaptureDeviceNames", Module)]
    private static partial string[] GetCaptureDeviceNamesInternal();

    public static IEnumerable<string> GetStringList(GetEnumerationStringList param) => GetCaptureDeviceNamesInternal();

    public static ALCaptureDevice CaptureOpenDevice(string devicename, int frequency, ALFormat format, int buffersize) => ALCaptureDevice.Null;

    public static void CaptureStart(ALCaptureDevice device)
    {
    }

    public static void CaptureStop(ALCaptureDevice device)
    {
    }
}
