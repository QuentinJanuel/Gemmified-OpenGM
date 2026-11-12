using OpenGM.IO;
using System.Text.RegularExpressions;

namespace OpenGM;

public static class CompatFlags
{
    /// <summary>
    /// Prior to GMS2, depth rendering was forced to 0 - same as layer_force_draw_depth.
    /// </summary>
    [GMCompatFlag(before: "2.0")]
    public static bool ForceDepthZero = false;

    /// <summary>
    /// Whether # should be treated as a newline or not in text rendering.
    /// </summary>
    [GMCompatFlag(before: "2.0")]
    public static bool HashNewlines = false;

    /// <summary>
    /// If true, script_execute will return 0 by default instead of null.
    /// </summary>
    [GMCompatFlag(before: "2.3")]
    public static bool ZeroReturnValue = false;

    /// <summary>
    /// If true, a variable holding a plain value can be read as a one-element array: <c>a[0]</c> is
    /// <c>a</c>.
    /// </summary>
    /// <remarks>
    /// Before 2.3, GML has no separate array type -- a variable and the first element of an array
    /// of the same name are the same storage, which OpenGMK states outright in its own accessor
    /// ("Variables with 0D arrays, and ones with no array accessor, implicitly refer to `x[0]`").
    /// So a game is free to store a single key in <c>global.upButton</c> and read it back as
    /// <c>global.upButton[0]</c>, and I Wanna Be The Ocean Princess' scrButtonCheck does exactly
    /// that for every input it polls, every step.
    ///
    /// From 2.3 arrays are a real type and indexing a number is a genuine error, so this stays off
    /// there and such a read still throws rather than quietly answering something.
    /// </remarks>
    [GMCompatFlag(before: "2.3")]
    public static bool ScalarIsFirstArrayElement = false;

    /// <summary>
    /// If true, scripts are passed using 0-based asset ids, instead of instance-like ids (starting at 10000).
    /// </summary>
    [GMCompatFlag(before: "2.3")]
    public static bool ScriptAssetIds = false;

    /// <summary>
    /// If true, instances on the same depth are sorted using ThenByDescending instead of ThenBy.
    /// </summary>

    // todo: is this the right version?

    // 1.0.0.9999                   OrderByDescending(depth), ThenByDescending(instanceid)
    // 2.2.0.258 / SURVEY_PROGRAM   OrderByDescending(depth), ThenByDescending(instanceid)

    // // LTS 2022.0.0.12           OrderByDescending(depth), ThenBy(instanceid)
    // // LTS 2022.0.3.99           OrderByDescending(depth), ThenBy(instanceid)
    // // 2024.13.1.242             OrderByDescending(depth), ThenBy(instanceid)

    [GMCompatFlag(before: "2.3")]
    public static bool DepthSortingReverseInstanceIds = false;

    /// <summary>
    /// The older <c>ds_map</c> implementation: <c>ds_map_write</c> emits the pre-Studio text format
    /// rather than the binary one, and the hash table has 4096 buckets rather than 256.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves matter, and the second one is the surprising one. A real runner iterates a
    /// <c>ds_map</c> from its highest bucket down, where the bucket is
    /// <c>crc32(key) mod tableSize</c> and ties fall in insertion order -- and
    /// <c>ds_map_write</c> serialises in that order. Games check their saves against it
    /// (<c>md5_string_unicode(ds_map_write(map) + salt)</c>), so a differently-sized table produces
    /// a different checksum for identical data and the save is rejected as corrupt.
    /// </para>
    /// <para>
    /// Measured, both halves, from two runtimes:
    /// </para>
    /// <list type="bullet">
    /// <item>1.4.9999 -- binary format, 256 buckets. Reproduces all five orderings printed by
    /// a probe project, including a 32-key set of real save-field names.</item>
    /// <item>1.4.1757 -- text format, 4096 buckets. Reproduces the 33-key ordering inside a save
    /// file written by a real install of I Wanna Spook Jam, and with it that game's own stored
    /// checksum.</item>
    /// </list>
    /// <para>
    /// The boundary is only bracketed, not known: it is somewhere in (1757, 9999]. 9999 is used
    /// because it is the later of the two observations, so both are honoured; a game built in
    /// between gets the old behaviour, which is the likelier of the two for most games.
    /// </para>
    /// </remarks>
    [GMCompatFlag(before: "1.0.0.9999")]
    public static bool LegacyDataStructures = false;

    /// <summary>
    /// GameMaker Studio 1.x seeds its WELL512 generator with a *signed* running value, so the shift
    /// in the seeding loop sign-extends; the result is masked to 31 bits and fed back. 2.x uses an
    /// unsigned value throughout. The step function is identical either way -- only the initial
    /// state differs, and only from the first LCG result with its high bit set onwards, at which
    /// point the whole stream diverges. See <see cref="GMRandom.InitRandom"/>.
    ///
    /// Verified against GMS 1.4.9999: 867/867 captured draws reproduce exactly.
    /// </summary>
    [GMCompatFlag(before: "2.0")]
    public static bool LegacyRandomSeeding = false;

    /// <summary>
    /// Legacy bounding box and collision logic, using integer math over doubles.
    /// </summary>
    [GMCompatFlag(before: "2022.1")]
    public static bool LegacyCollision = false;

    /// <summary>
    /// Prior to GMS2.3, arrays are value types rather than references: assigning one to another
    /// variable gives that variable its own array, so a plain "arr[i] = value" write must not be
    /// visible through whatever it was assigned from. See <see cref="VirtualMachine.GMLArray"/>.
    /// </summary>
    [GMCompatFlag(before: "2.3")]
    public static bool LegacyArrayCopyOnWrite = false;

    // --------------------------------------

    /// <summary>
    /// Applies the pre-GMS2 "# is a newline" text convention if <see cref="HashNewlines"/> is set,
    /// otherwise returns the string unchanged.
    /// </summary>
    public static string ApplyHashNewlines(string text)
    {
        if (!HashNewlines || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Replace \# with #, replace # with newlines.
        text = Regex.Replace(text, @"(?<=[^\\]|^)#", Environment.NewLine);
        text = text.Replace(@"\#", "#");
        return text;
    }

    public static Dictionary<string, bool> ForcedValues = [];

    public static void Init()
    {
        var fields = typeof(CompatFlags).GetFields()
            .Where(prop => prop.IsDefined(typeof(GMCompatFlagAttribute), false));

        foreach (var fieldInfo in fields)
        {
            var attributes = (GMCompatFlagAttribute[])fieldInfo.GetCustomAttributes(typeof(GMCompatFlagAttribute), false);
            foreach (var attribute in attributes)
            {
                if (attribute.BeforeVersion is null && attribute.SinceVersion is null)
                {
                    continue;
                }

                if (attribute.SinceVersion is not null && VersionManager.EngineVersion < attribute.SinceVersion)
                {
                    continue;
                }

                var isAtOrAfter = attribute.BeforeVersion is not null && VersionManager.EngineVersion >= attribute.BeforeVersion;
                if (isAtOrAfter)
                {
                    continue;
                }

                fieldInfo.SetValue(null, attribute.NewValue);
            }

            if (ForcedValues.TryGetValue(fieldInfo.Name, out var value))
            {
                fieldInfo.SetValue(null, value);
            }
            var currentValue = fieldInfo.GetValue(null);

            DebugLog.LogInfo($"Compat flag \"{fieldInfo.Name}\": {currentValue}");
        }

        if (ForceDepthZero)
        {
            GraphicsManager.ForceDepth = true;
            GraphicsManager.ForcedDepth = 0;
        }
    }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class GMCompatFlagAttribute(
    string? since = null,
    string? before = null,
    bool value = true
) : Attribute
{
    public Version? SinceVersion { get; private set; } = (since != null) ? new(since) : null;
    public Version? BeforeVersion { get; private set; } = (before != null) ? new(before) : null;
    public bool NewValue { get; private set; } = value;
}
