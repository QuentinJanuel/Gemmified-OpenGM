using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenGM.Tests;

/// <summary>
/// A buffer a game writes must be a buffer a game can read, and must still be there next time.
///
/// <c>buffer_save</c> used to go straight through <c>File.WriteAllBytes</c> at the game folder,
/// around the persisted layer everything else writes to. On the browser build that lands in the
/// wasm's in-memory filesystem: gone on reload, invisible to the page's file browser, and not where
/// a read looks first. GameMaker Studio games usually save through a buffer, and the mod SDK hands
/// a sprite to one the same way -- which is how a mod came to write a picture it could not then
/// load.
/// </summary>
[TestClass]
public class BufferPersistenceTests
{
    private static readonly byte[] Payload = [0x89, 0x50, 0x4e, 0x47, 0x00, 0xff, 0x10];

    private static void InAGameFolder(Action<string> body)
    {
        var dir = Directory.CreateTempSubdirectory("opengm-buffer-").FullName;
        var before = Entry.DataWinFolder;
        Entry.DataWinFolder = dir;
        try
        {
            body(dir);
        }
        finally
        {
            Entry.DataWinFolder = before;
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ASavedBufferLoadsBack()
    {
        InAGameFolder(_ =>
        {
            var written = BufferManager.CreateBuffer(Payload.Length, BufferType.Grow, 1, Payload);
            BufferManager.SaveBuffer(written, "thing.dat");

            var read = BufferManager.LoadBuffer("thing.dat");

            Assert.AreNotEqual(-1, read, "the file a game just wrote could not be found");
            CollectionAssert.AreEqual(Payload, BufferManager.Buffers[read].Data);
        });
    }

    /// <summary>
    /// Games write into a subfolder without ever calling <c>directory_create</c> -- GameMaker's
    /// sandbox materialises it as part of the write.
    /// </summary>
    [TestMethod]
    public void ASubfolderIsCreatedByTheWrite()
    {
        InAGameFolder(dir =>
        {
            var written = BufferManager.CreateBuffer(Payload.Length, BufferType.Grow, 1, Payload);
            BufferManager.SaveBuffer(written, "Data/slot1.sav");

            Assert.IsTrue(File.Exists(Path.Combine(dir, "Data", "slot1.sav")));
            Assert.AreNotEqual(-1, BufferManager.LoadBuffer("Data/slot1.sav"));
        });
    }

    [TestMethod]
    public void LoadingSomethingThatIsNotThereSaysSoRatherThanThrowing()
    {
        InAGameFolder(_ => Assert.AreEqual(-1, BufferManager.LoadBuffer("never-written.dat")));
    }

    /// <summary>
    /// The picture the mod SDK transfers: written as a buffer, loaded back as a sprite. Those are
    /// two different subsystems, and they have to agree about where the file is -- they did not,
    /// and a published mod's sprite stopped loading.
    /// </summary>
    [TestMethod]
    public void WhatABufferWroteIsWhereASpriteLooks()
    {
        InAGameFolder(_ =>
        {
            var written = BufferManager.CreateBuffer(Payload.Length, BufferType.Grow, 1, Payload);
            BufferManager.SaveBuffer(written, "fpspr_mod_slot.png");

            var found = OpenGM.IO.GMFileSystemExtension.ReadGameFile(
                Entry.ResolveDataWinPath("fpspr_mod_slot.png")
            );

            CollectionAssert.AreEqual(Payload, found);
        });
    }
}
