namespace OpenTK.Audio.OpenAL;

/// <summary>
/// Namespace-shadowed replacements for the handful of real OpenTK.Audio.OpenAL enum/struct types
/// AudioManager.cs and SoundFunctions.cs reference by name -- same "compile against a same-named
/// stand-in" trick as OpenTKShim/GLEnums.cs, scoped to only the members those two files actually
/// use (confirmed via grep, not guessed).
/// </summary>
public enum ALFormat
{
    Mono8,
    Mono16,
    Stereo8,
    Stereo16,
    MonoFloat32Ext,
    StereoFloat32Ext,
}

public enum ALSourcei
{
    Buffer,
}

public enum ALGetSourcei
{
    Buffer,
    SourceState,
}

public enum ALSourceb
{
    Looping,
}

public enum ALSourcef
{
    Gain,
    Pitch,
    SecOffset,
    MaxDistance,
    ReferenceDistance,
    RolloffFactor,
}

public enum ALSource3f
{
    Position,
    Velocity,
}

public enum ALGetBufferi
{
    Size,
    Channels,
    Bits,
    Frequency,
}

public enum ALListener3f
{
    Position,
    Velocity,
}

public enum ALListenerf
{
    Gain,
}

public enum ALListenerfv
{
    Orientation,
}

public enum ALSourceState
{
    Initial,
    Playing,
    Paused,
    Stopped,
}

public enum ALError
{
    NoError,
}

public enum AlcError
{
    NoError,
}

public enum ALGetString
{
    Version,
    Vendor,
    Extensions,
}

public enum ALDistanceModel
{
    None,
    InverseDistance,
    InverseDistanceClamped,
    LinearDistance,
    LinearDistanceClamped,
    ExponentDistance,
    ExponentDistanceClamped,
}

public enum GetEnumerationStringList
{
    CaptureDeviceSpecifier,
}

public struct ALDevice
{
    internal int Handle;
    public static readonly ALDevice Null = default;
}

public struct ALContext
{
    internal int Handle;
    public static readonly ALContext Null = default;
}

public struct ALContextAttributes
{
}

public struct ALCaptureDevice
{
    internal int Handle;
    public static readonly ALCaptureDevice Null = default;
}
