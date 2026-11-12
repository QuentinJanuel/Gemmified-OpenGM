using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The file_bin_* family, which was eight bare <c>// name</c> comments.
///
/// The thing these assert that isn't obvious from the API is <b>where the bytes go</b>. Every
/// previous file family in this engine started out writing through System.IO, which under the
/// browser build is an in-memory filesystem that vanishes on reload -- so saving appeared to work
/// and silently didn't. That was fixed once for file_text_* and again for ini_*; these go straight
/// to the same persisted store, and <see cref="SurvivesACloseAndReopen"/> is what pins it.
/// </summary>
[TestClass]
public class FileBinaryTests
{
    // A distinct filename per test rather than deleting between them: the persisted store's
    // removal helper is internal, and isolation by name is just as good here.
    private static int _counter;
    private string Path = null!;

    [TestInitialize]
    public void SetUp() => Path = $"opengm_test_bin_{_counter++}.dat";

    private int Open(int mode) => FileFunctions.file_bin_open([Path, mode]).Conv<int>();
    private static int Size(int f) => FileFunctions.file_bin_size([f]).Conv<int>();
    private static int Pos(int f) => FileFunctions.file_bin_position([f]).Conv<int>();
    private static int Read(int f) => FileFunctions.file_bin_read_byte([f]).Conv<int>();
    private static void Write(int f, int b) => FileFunctions.file_bin_write_byte([f, b]);
    private static void Close(int f) => FileFunctions.file_bin_close([f]);

    [TestMethod]
    public void SurvivesACloseAndReopen()
    {
        var w = Open(1);
        Assert.IsTrue(w >= 0);
        Write(w, 65);
        Write(w, 66);
        Write(w, 67);
        Assert.AreEqual(3, Size(w));
        Assert.AreEqual(3, Pos(w));
        Close(w);

        var r = Open(0);
        Assert.IsTrue(r >= 0, "the file has to still be there after closing");
        Assert.AreEqual(3, Size(r));
        Assert.AreEqual(0, Pos(r), "a fresh handle starts at the beginning");
        Assert.AreEqual(65, Read(r));
        Assert.AreEqual(66, Read(r));
        Assert.AreEqual(67, Read(r));
        Close(r);
    }

    [TestMethod]
    public void ReadingAMissingFileFails()
    {
        Assert.AreEqual(-1, Open(0));
    }

    [TestMethod]
    public void WriteModeTruncatesAndReadWriteDoesNot()
    {
        var w = Open(1);
        Write(w, 1);
        Write(w, 2);
        Close(w);

        // Mode 1 on an existing file starts empty...
        var w2 = Open(1);
        Assert.AreEqual(0, Size(w2));
        Close(w2);

        // ...and that truncation persists even though nothing was written through the handle.
        var r = Open(0);
        Assert.AreEqual(0, Size(r));
        Close(r);

        w = Open(1);
        Write(w, 9);
        Close(w);

        // Mode 2 keeps what's there.
        var rw = Open(2);
        Assert.AreEqual(1, Size(rw));
        Assert.AreEqual(9, Read(rw));
        Close(rw);
    }

    [TestMethod]
    public void SeekMovesTheReadAndWriteCursor()
    {
        var w = Open(1);
        foreach (var b in new[] { 10, 20, 30, 40 })
        {
            Write(w, b);
        }
        Close(w);

        var f = Open(2);
        FileFunctions.file_bin_seek([f, 2]);
        Assert.AreEqual(2, Pos(f));
        Assert.AreEqual(30, Read(f));

        // Overwrite in place rather than appending.
        FileFunctions.file_bin_seek([f, 1]);
        Write(f, 99);
        Assert.AreEqual(4, Size(f), "an in-place write must not grow the file");
        FileFunctions.file_bin_seek([f, 1]);
        Assert.AreEqual(99, Read(f));
        Close(f);
    }

    [TestMethod]
    public void WritingPastTheEndZeroFillsTheGap()
    {
        var f = Open(1);
        Write(f, 7);
        FileFunctions.file_bin_seek([f, 4]);
        Write(f, 8);

        Assert.AreEqual(5, Size(f));
        FileFunctions.file_bin_seek([f, 0]);
        CollectionAssert.AreEqual(new[] { 7, 0, 0, 0, 8 },
            new[] { Read(f), Read(f), Read(f), Read(f), Read(f) });
        Close(f);
    }

    [TestMethod]
    public void ReadingPastTheEndIsZeroRatherThanAnError()
    {
        // DELIBERATE DIVERGENCE, measured: GMS 1.4.9999 raises a fatal "Error reading byte." here
        // and stops the game -- it killed a probe run that tried to observe what the call returned.
        //
        // We return 0 and log a warning instead. Reproducing the abort would only ever matter to a
        // game that already crashes on Windows, whereas an off-by-one in our own EOF accounting
        // would turn a working game into a crashing one. Same trade as the pre-1970 dates.
        var w = Open(1);
        Write(w, 42);
        Close(w);

        var r = Open(0);
        Assert.AreEqual(42, Read(r));
        Assert.AreEqual(0, Read(r));
        Assert.AreEqual(2, Pos(r), "the position still advances past the end");
        Close(r);
    }

    [TestMethod]
    public void RewriteTruncatesInPlace()
    {
        var w = Open(1);
        Write(w, 1);
        Write(w, 2);
        Write(w, 3);
        Close(w);

        var f = Open(2);
        Assert.AreEqual(3, Size(f));
        FileFunctions.file_bin_rewrite([f]);
        Assert.AreEqual(0, Size(f));
        Assert.AreEqual(0, Pos(f));
        Write(f, 5);
        Close(f);

        var r = Open(0);
        Assert.AreEqual(1, Size(r));
        Assert.AreEqual(5, Read(r));
        Close(r);
    }

    [TestMethod]
    public void HandlesAreIndependentAndReused()
    {
        var a = Open(1);
        var b = FileFunctions.file_bin_open(["other_test_bin.dat", 1]).Conv<int>();
        Assert.AreNotEqual(a, b);

        Close(a);
        // The closed id becomes available again rather than climbing forever.
        var c = Open(2);
        Assert.AreEqual(a, c);
        Close(c);
        Close(b);
    }

    [TestMethod]
    public void ReadOnlyHandlesIgnoreWrites()
    {
        var w = Open(1);
        Write(w, 3);
        Close(w);

        var r = Open(0);
        Write(r, 99);
        Assert.AreEqual(1, Size(r), "a read handle must not be writable");
        Close(r);

        var check = Open(0);
        Assert.AreEqual(3, Read(check));
        Close(check);
    }
}
