using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// The GMFileSystem extension's FS_file_text_* family, which is GMFile.dll standing in for GM's own
/// file_text_* API.
///
/// What these pin is that a read is a *line*, not the rest of the file. FS_file_text_read_string
/// used to return everything from the cursor to EOF, which silently corrupted any game whose format
/// has more below the line it parses -- see <see cref="ReadStringStopsAtTheEndOfTheLine"/> and the
/// jtool round trip under it.
/// </summary>
[TestClass]
public class GMFileSystemTextTests
{
    // A distinct filename per test rather than deleting between them, same as FileBinaryTests.
    private static int _counter;
    private string Path = null!;

    [TestInitialize]
    public void SetUp() => Path = $"opengm_test_text_{_counter++}.txt";

    private int OpenWrite() => GMFileSystemExtension.FS_file_text_open_write([Path]).Conv<int>();
    private int OpenRead() => GMFileSystemExtension.FS_file_text_open_read([Path]).Conv<int>();
    private static void Write(int f, string s) => GMFileSystemExtension.FS_file_text_write_string([f, s]);
    private static void WriteLn(int f) => GMFileSystemExtension.FS_file_text_writeln([f]);
    private static void Close(int f) => GMFileSystemExtension.FS_file_text_close([f]);
    private static string ReadString(int f) => GMFileSystemExtension.FS_file_text_read_string([f]).Conv<string>();
    private static string ReadLn(int f) => GMFileSystemExtension.FS_file_text_readln([f]).Conv<string>();
    private static double ReadReal(int f) => GMFileSystemExtension.FS_file_text_read_real([f]).Conv<double>();
    private static bool Eof(int f) => GMFileSystemExtension.FS_file_text_eof([f]).Conv<bool>();
    private static bool Eoln(int f) => GMFileSystemExtension.FS_file_text_eoln([f]).Conv<bool>();

    private void WriteLines(params string[] lines)
    {
        var f = OpenWrite();
        foreach (var line in lines)
        {
            Write(f, line);
            WriteLn(f);
        }
        Close(f);
    }

    [TestMethod]
    public void ReadStringStopsAtTheEndOfTheLine()
    {
        WriteLines("first", "second");

        var f = OpenRead();
        Assert.AreEqual("first", ReadString(f), "a read is one line, not the rest of the file");

        // ...and it leaves the cursor *on* the line break rather than past it: advancing is
        // file_text_readln's job, so a second read with no readln between them stays put.
        Assert.IsTrue(Eoln(f));
        Assert.AreEqual("", ReadString(f));

        ReadLn(f);
        Assert.AreEqual("second", ReadString(f));
        Close(f);
    }

    /// <summary>
    /// The bug this family was found by, end to end.
    ///
    /// A jtool map is one dense line -- "jtool|1.3.1|...|objects:&lt;base32&gt;" -- and loadMap reads it
    /// with a single FS_file_text_read_string, splits it on "|", and decodes the "objects:" section
    /// three characters at a time into instance_create calls. Since jtool 1.2.0 the save also
    /// appends a second, human-readable copy of the map below it, and none of that copy contains a
    /// "|", so when the read returned the whole file it all landed inside the final section. The
    /// decoder walked straight out of the base32 and into the prose, and every three characters of
    /// it that happened to decode became an object: save a map, reload it, and it came back with
    /// blocks and spikes nobody placed.
    /// </summary>
    [TestMethod]
    public void AJtoolMapReloadsWithoutItsTrailingCopy()
    {
        var f = OpenWrite();
        Write(f, "jtool|1.3.1|inf:0|dot:0|objects:-6g7ng7n03m0");
        WriteLn(f);
        WriteLn(f);
        Write(f, "data repeated below for easy parsing by other tools");
        WriteLn(f);
        Write(f, "objects: (x, y, type)");
        WriteLn(f);
        Write(f, "768 448 1 448 1 320 448 1 ");
        WriteLn(f);
        Write(f, "version:1.3.1");
        WriteLn(f);
        Close(f);

        var r = OpenRead();
        var content = ReadString(r);
        Close(r);

        Assert.AreEqual("jtool|1.3.1|inf:0|dot:0|objects:-6g7ng7n03m0", content);

        // The part that actually placed the phantom objects: everything jtool hands its base32
        // decoder is the tail of the last "|" section, and that has to end with the encoded map.
        var lastSection = content[(content.LastIndexOf('|') + 1)..];
        Assert.AreEqual("objects:-6g7ng7n03m0", lastSection);
    }

    [TestMethod]
    public void ReadRealReadsTheLineAsANumber()
    {
        WriteLines("109", "567.4000244140625", "not a number");

        var f = OpenRead();
        Assert.AreEqual(109d, ReadReal(f));
        ReadLn(f);
        Assert.AreEqual(567.4000244140625d, ReadReal(f), "full precision, and '.' is the decimal point");
        ReadLn(f);
        Assert.AreEqual(0d, ReadReal(f), "an unparseable line reads as 0, not as a throw");
        Close(f);
    }

    [TestMethod]
    public void ReadlnWalksTheFileToEof()
    {
        WriteLines("a", "b", "c");

        var f = OpenRead();
        Assert.IsFalse(Eof(f));
        Assert.AreEqual("a", ReadLn(f));
        Assert.AreEqual("b", ReadLn(f));
        Assert.AreEqual("c", ReadLn(f));
        Assert.IsTrue(Eof(f));
        Close(f);
    }

    [TestMethod]
    public void ACarriageReturnEndsALineToo()
    {
        // Nothing in this engine writes CRLF, but a map carried over from a Windows install does.
        var f = OpenWrite();
        Write(f, "first\r\nsecond");
        Close(f);

        var r = OpenRead();
        Assert.AreEqual("first", ReadString(r), "the CR is a line break, not part of the string");

        // readln returns what is *left* of the current line -- nothing, the read above took it --
        // and steps over the whole CRLF rather than landing between the two characters.
        Assert.AreEqual("", ReadLn(r));
        Assert.AreEqual("second", ReadString(r));
        Close(r);
    }
}
