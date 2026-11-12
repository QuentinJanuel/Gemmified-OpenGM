namespace OpenGM;

/// <summary>
/// Backs GMFileSystemExtension's file I/O in the browser with the game's <see cref="Vfs"/>, which
/// <see cref="Gems"/> builds over the host's replica when the host's init arrives.
///
/// Every path here is a FULL path ("/game/Save/save1", "/appdata/MyGame/config.ini"), never a
/// basename: the VFS resolves a path into a mount plus segments, so `Save\game1` and
/// `Backup\game1` stay two files.
/// </summary>
internal static class BrowserFileSystem
{
    private static Vfs Files => Gems.Files ?? throw new InvalidOperationException("the host has not sent init yet");

    public static byte[]? Load(string path) => Files.Read(Vfs.ParsePath(path));

    public static void Save(string path, byte[] bytes) => Files.Write(Vfs.ParsePath(path), bytes);

    public static bool Exists(string path) => Files.Exists(Vfs.ParsePath(path));

    /// <returns>Whether anything was there to delete, which is what GML's file_delete reports.</returns>
    public static bool Remove(string path) => Files.Remove(Vfs.ParsePath(path));

    /// <summary>Whether this directory was created, or any file lives under it.</summary>
    public static bool DirectoryExists(string path) => Files.DirectoryExists(Vfs.ParsePath(path));

    /// <summary>
    /// GML's directory_create. Recorded explicitly, because a directory is otherwise inferred from
    /// the files inside it, and inference cannot represent an empty one.
    /// </summary>
    public static void CreateDirectory(string path) => Files.CreateDirectory(Vfs.ParsePath(path));

    public static void RemoveDirectory(string path) => Files.RemoveDirectory(Vfs.ParsePath(path));
}
