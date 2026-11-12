using OpenGM.IO;
using OpenGM.Loading;
using System.Reflection;

namespace OpenGM;

public static class LoadSave
{
    // Handles all the file-system sandboxing stuff that GM does
    // C++ runner used as reference, replaced with easier C# function calls where possible

    // TODO: is this linux/mac compatible?

    public static bool SaveFileExists(string _pszFileName)
    {
        var _name = "";
        GetSaveFileName(ref _name, 2048, _pszFileName);
        return FileExists(_name);
    }

    public static bool BundleFileExist(string _pszFileName)
    {
        var _name = "";
        GetBundleFileName(ref _name, 2048, _pszFileName);
        return FileExists(_name);
    }

    public static bool FileExists(string _pFilename)
    {
        return File.Exists(_pFilename);
    }

    public static int GetBundleFileName(ref string name, int size, string _pszFileName)
    {
        name = "";

        if (string.IsNullOrEmpty(_pszFileName))
        {
            return -1;
        }

        // No game directory to sandbox against means nothing can resolve, and assigning an empty
        // CurrentDirectory below throws -- which would abort the whole event the caller is in
        // rather than just reporting the file as absent.
        if (string.IsNullOrEmpty(GetFilePrePend()))
        {
            return -1;
        }

        var savedCurrentDir = Environment.CurrentDirectory;
        Environment.CurrentDirectory = GetFilePrePend();

        var fullPath = Path.GetFullPath(_pszFileName);
        var currentDirectory = Environment.CurrentDirectory;
        var filePrePend = GetFilePrePend();

        Environment.CurrentDirectory = savedCurrentDir;

        if (fullPath.StartsWith(currentDirectory))
        {
            name = "";
            name += filePrePend;
            name += fullPath[currentDirectory.Length..];
            return 0;
        }

        if (fullPath.StartsWith(filePrePend))
        {
            name = "";
            name += filePrePend;
            name += fullPath[filePrePend.Length..];
            return 0;
        }

        DebugLog.LogError($"Not allowing file operation at {fullPath}");
        return 0;
    }

    public static int GetSaveFileName(ref string name, int size, string _pszFileName)
    {
        name = "";

        if (string.IsNullOrEmpty(_pszFileName))
        {
            return -1;
        }

        // No game directory to sandbox against means nothing can resolve, and assigning an empty
        // CurrentDirectory below throws -- which would abort the whole event the caller is in
        // rather than just reporting the file as absent.
        if (string.IsNullOrEmpty(GetFilePrePend()))
        {
            return -1;
        }

        var savedCurrentDir = Environment.CurrentDirectory;
        Environment.CurrentDirectory = GetFilePrePend();

        var fullPath = Path.GetFullPath(_pszFileName);
        var currentDirectory = Environment.CurrentDirectory;
        // check whitelisted
        var savePrepend = GetSavePrePend();

        Environment.CurrentDirectory = savedCurrentDir;

        if (fullPath.StartsWith(currentDirectory))
        {
            name = "";
            name += savePrepend;
            name += fullPath[currentDirectory.Length..];
            return 0;
        }

        var workingDirectory = GetFilePrePend();
        if (fullPath.StartsWith(workingDirectory))
        {
            name = "";
            name += savePrepend;
            name += fullPath[workingDirectory.Length..];
            return 0;
        }

        if (fullPath.StartsWith(savePrepend))
        {
            name = "";
            name += savePrepend;
            name += fullPath[savePrepend.Length..];
            return 0;
        }

        /*if (fullPath.StartsWith(g_pPrevSaveDirectory))
        {
            name = "";
            name += g_pPrevSaveDirectory;
            name += fullPath[g_pPrevSaveDirectory.Length..];
            return 0;
        }*/

        DebugLog.LogError($"Not allowing file operation at {fullPath}");
        return 0;
    }

    public static string g_pSavePrePend = "";
    public static string g_pFilePrePend = "";

    /// <summary>
    /// Resolves a GML-supplied path into the game's SAVE AREA, which under GameMaker Studio's
    /// sandbox is where every game write actually lands: %LOCALAPPDATA%\&lt;game name&gt;\, with the
    /// path's position relative to the game folder preserved. A game writing "Data\save1" ends up
    /// with %LOCALAPPDATA%\&lt;game&gt;\Data\save1, NOT a file beside its own .exe.
    ///
    /// This is the same rule GetSaveFileName implements, expressed without the
    /// Environment.CurrentDirectory + Path.GetFullPath dance that one performs. That dance does not
    /// survive browser-wasm: there is no meaningful process working directory, and on a
    /// unix-semantics target a backslash is an ordinary filename character, so a game's
    /// "Data\save1" would never be recognised as being inside the game folder at all.
    ///
    /// Getting this right is what makes the page's file browser tell the truth. A player who wants
    /// to move a save onto a real Windows install has to be shown the location it actually belongs
    /// in, and "next to the .exe" vs "in application data" is exactly the distinction that matters.
    /// </summary>
    public static string? ResolveSavePath(string path) => ResolveSavePath(path, StoreHas);

    /// <summary>
    /// <see cref="ResolveSavePath(string)"/> with the store lookup supplied, so the legacy fallback
    /// is testable off the browser build. Null means the write is refused (the GEMS save-path rules
    /// for gms1/gms2: rooted paths, other drives, and app-data folders outside &lt;Name&gt;).
    /// </summary>
    public static string? ResolveSavePath(string path, Func<string, bool> storeHas)
    {
        var resolved = ResolveSavePathSpec(path);
        var legacy = ResolveSavePathLegacy(path);
        if (resolved == legacy)
        {
            return resolved;
        }

        // The legacy mapping put saves elsewhere (a drive path became <Name>/C:/...). Keep finding and
        // updating those, and new files beside them, so no existing save goes missing; only paths with
        // nothing stored there follow the rules.
        var legacyDir = legacy[..legacy.LastIndexOf('/')];
        var savePrepend = GetSavePrePend().Replace('\\', '/');
        var legacyStored = storeHas(legacy) || (legacyDir + "/" != savePrepend && storeHas(legacyDir));
        return (resolved == null || !storeHas(resolved)) && legacyStored ? legacy : resolved;
    }

    private static bool StoreHas(string path)
    {
#if BROWSER
        return BrowserFileSystem.Exists(path) || BrowserFileSystem.DirectoryExists(path);
#else
        return File.Exists(path) || Directory.Exists(path);
#endif
    }

    private static readonly string[] TempMarkers = ["appdata/local/temp/", "local settings/temp/"];
    private static readonly string[] AppDataMarkers = ["appdata/local/", "appdata/roaming/", "local settings/application data/", "application data/"];

    /// <summary>The GEMS Windows path mapping plus the gms1/gms2 write rules; null when refused.</summary>
    private static string? ResolveSavePathSpec(string path)
    {
        var normalized = path.Replace('\\', '/');
        var savePrepend = GetSavePrePend().Replace('\\', '/');
        var saveRoot = savePrepend.TrimEnd('/');
        if (normalized.StartsWith(savePrepend, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.Equals(saveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return savePrepend;
        }

        var relative = Under(normalized, GetFilePrePend().Replace('\\', '/').TrimEnd('/')) ?? Under(normalized, "c:/game");
        if (relative == null)
        {
            var lower = normalized.ToLowerInvariant();
            if (TempMarkers.Any(lower.Contains))
            {
                return null;
            }

            var marker = AppDataMarkers
                .Select(m => (at: lower.IndexOf(m, StringComparison.Ordinal), m.Length))
                .Where(x => x.at >= 0)
                .OrderBy(x => x.at)
                .FirstOrDefault((at: -1, 0));
            if (marker.at >= 0)
            {
                var name = saveRoot[(saveRoot.LastIndexOf('/') + 1)..];
                var inName = Under(normalized[(marker.at + marker.Item2)..], name);
                return inName == null ? null : savePrepend + inName;
            }

            if (normalized.StartsWith('/') || (normalized.Length > 1 && normalized[1] == ':'))
            {
                return null;
            }

            relative = normalized;
        }

        return savePrepend + relative.TrimStart('/');
    }

    private static string? Under(string path, string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        if (path.Equals(dir, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return path.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase) ? path[(dir.Length + 1)..] : null;
    }

    /// <summary>The legacy mapping, kept only to find saves it already wrote.</summary>
    private static string ResolveSavePathLegacy(string path)
    {
        var normalized = path.Replace('\\', '/');
        var savePrepend = GetSavePrePend().Replace('\\', '/');

        // Already in the save area -- don't prepend it twice.
        if (normalized.StartsWith(savePrepend, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        // Rooted inside the game folder (a path a game built from working_directory): make it
        // relative before re-rooting it at the save area.
        var filePrepend = GetFilePrePend().Replace('\\', '/');
        if (!string.IsNullOrEmpty(filePrepend) && normalized.StartsWith(filePrepend, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[filePrepend.Length..];
        }

        return savePrepend.TrimEnd('/') + "/" + normalized.TrimStart('/');
    }

    public static string GetSavePrePend()
    {
        if (!string.IsNullOrEmpty(g_pSavePrePend)) // or empty g_pGameProjectName
        {
            return g_pSavePrePend;
        }

        // TODO: check flags in header to use appdata instead of local

#if BROWSER
        // The shared VFS's `appdata` mount root. GameMaker Studio games save here by default
        // (game_save_id), and a browser has no %LOCALAPPDATA% -- Environment.GetFolderPath returns
        // something meaningless under wasm, which would land these files in a bogus subfolder of
        // the game mount instead of the application-data one the file browser shows separately.
        g_pSavePrePend = "/appdata/" + GameLoader.GeneralInfo.Name + "/";
#else
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        g_pSavePrePend = Path.Combine(folder, GameLoader.GeneralInfo.Name) + Path.DirectorySeparatorChar;
#endif
        return g_pSavePrePend;
    }

    public static string GetFilePrePend()
    {
        if (!string.IsNullOrEmpty(g_pFilePrePend))
        {
            return g_pFilePrePend;
        }

        g_pFilePrePend = Path.GetDirectoryName(Entry.GamePath)!;
        return g_pFilePrePend;
    }

    public static bool RemoveSaveFile(string _pszFileName)
    {
        var _name = "";
        GetSaveFileName(ref _name, 2048, _pszFileName);

        try
        {
            File.Delete(_name);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
