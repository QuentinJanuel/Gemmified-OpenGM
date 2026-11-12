using MemoryPack;
using OpenGM.VirtualMachine;

namespace OpenGM.SerializedFiles;

/// <summary>
/// A timeline resource: a set of code blocks ("moments"), each keyed to the step at which it runs
/// on whichever instance is playing the timeline. An instance plays one via its timeline_index /
/// timeline_position / timeline_running state, stepped once a frame by
/// <see cref="GamemakerObject.UpdateTimeline"/>.
///
/// CTimeLineGM in cpp.
/// </summary>
[MemoryPackable]
public partial class Timeline
{
    public int AssetId;

    public string Name = null!;

    /// <summary>
    /// Moment step -> indices into <see cref="Loading.GameLoader.Codes"/>. Resolved into
    /// <see cref="Moments"/> at load time, the same way ObjectDefinition.FileStorage's script ids are.
    /// </summary>
    public Dictionary<int, List<int>> MomentCodeIDs = new();

    /// <summary>
    /// Moment step -> the actions at that step, in order. A list rather than a single code because
    /// one moment can hold several actions -- editor-authored moments compile to exactly one, but
    /// timeline_moment_add_script appends to whatever is already at that step.
    /// </summary>
    [MemoryPackIgnore]
    public Dictionary<int, List<VMCode>> Moments = new();
}
