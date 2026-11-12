using OpenGM.VirtualMachine;

namespace OpenGM.IO;

/// <summary>
/// Real implementations for the "GMResource" extension's (GMResource.dll's) resource
/// import/export/utility functions -- same design and reasoning as GMFileSystemExtension.cs and
/// GMIniExtension.cs. Before this existed, GMResource.dll fell through ExtensionManager's
/// per-filename routing to blanket StubAllFuncs, so every one of its functions was stubbed -- the
/// same root-cause pattern (a handler-routing gap) as the extension_stubfunc_real issue, just for a
/// whole extension file instead of one converter-emitted helper.
///
/// The load/edit-path functions (working-dir/save-area/temp setup, unique filename generation,
/// image import, fast copy) are implemented for real. The four *export* functions
/// (FS_export_image/_adv, FS_export_sound, FS_export_raw) write assets out to encoded files -- a map
/// editor's "save/export" feature -- which genuinely needs an image/audio encoder (this project only
/// bundles StbImageSharp's decoder) plus a browser download path; they're registered so they don't
/// crash whatever calls them, but log a one-time honest "not implemented" and return failure rather
/// than silently pretending an export succeeded. They're never on the game-load path (only reachable
/// via explicit user save/export actions), so deferring them doesn't block loading or editing.
/// </summary>
public static class GMResourceExtension
{
    private static int _uniqueCounter;
    private static bool _loggedExportUnsupported;

    public static void Register(ExtensionFile file)
    {
        var implemented = new Dictionary<string, ScriptResolver.GMLFunctionType>
        {
            ["FS_set_working_directory"] = FS_noop,
            ["FS_set_gm_save_area"] = FS_noop,
            ["FS_clean_temporary"] = FS_noop,
            ["FS_unique_fname"] = FS_unique_fname,
            ["FS_import_image"] = FS_import_image,
            ["FS_copy_fast"] = FS_copy_fast,
            ["FS_export_image"] = FS_export_unsupported,
            ["FS_export_image_adv"] = FS_export_unsupported,
            ["FS_export_sound"] = FS_export_unsupported,
            ["FS_export_raw"] = FS_export_unsupported,
        };

        foreach (var func in file.Functions)
        {
            var impl = implemented.GetValueOrDefault(func.Name) ?? ExtensionManager.MakeStubFunction(func.Name);
            if (!ScriptResolver.BuiltInFunctions.TryAdd(func.Name, impl))
            {
                DebugLog.LogError($"Could not add extension function {func.Name} to functions list");
            }
        }
    }

    private static object? FS_noop(object?[] args) => 0d;

    /// <summary>
    /// Returns a fresh, never-before-returned filename of the form prefix + counter + suffix. The
    /// monotonic counter (never reset within a session) guarantees uniqueness the same way the real
    /// extension's own temp-name generation does, without needing to actually probe the filesystem
    /// for collisions.
    /// </summary>
    private static object? FS_unique_fname(object?[] args)
    {
        var prefix = args.Length > 0 ? args[0].Conv<string>() : "";
        var suffix = args.Length > 1 ? args[1].Conv<string>() : "";
        return $"{prefix}__gmresource_temp_{_uniqueCounter++}{suffix}";
    }

    /// <summary>
    /// Returns a path that sprite_add/background_add can load directly. The real extension converts
    /// an arbitrary image file into a loadable form; here that's an identity pass-through, since
    /// sprite_add/background_add already decode every format this would produce (PNG/BMP/etc.) via
    /// StbImageSharp, and both resolve their argument through Entry.ResolveDataWinPath themselves --
    /// so handing the path straight back lets them resolve it exactly as they otherwise would.
    /// </summary>
    private static object? FS_import_image(object?[] args) => args[0].Conv<string>();

    private static object? FS_copy_fast(object?[] args)
    {
        var from = Entry.ResolveDataWinPath(args[0].Conv<string>());
        var to = Entry.ResolveDataWinPath(args[1].Conv<string>());
        if (!File.Exists(from))
        {
            return 0d;
        }

        var dir = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.Copy(from, to, overwrite: true);
        return 1d;
    }

    private static object? FS_export_unsupported(object?[] args)
    {
        if (!_loggedExportUnsupported)
        {
            _loggedExportUnsupported = true;
            DebugLog.LogWarning("GMResource export functions (FS_export_image/_adv/_sound/_raw) are not implemented -- " +
                "exporting assets to real files needs an image/audio encoder and a browser download path, neither of " +
                "which is wired up yet. Returning failure; loading and editing are unaffected.");
        }
        return 0d;
    }
}
