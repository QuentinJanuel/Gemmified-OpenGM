using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenGM.Tests;

/// <summary>
/// <c>buffer_string</c> is NUL-terminated UTF-8 and <c>buffer_text</c> is the same bytes without
/// the NUL. They were swapped, and written one byte per UTF-16 unit: a game sending a string down a
/// socket sent it unterminated, so the other end never saw where it ended. Writes also never
/// advanced the used size, and a grow buffer refused a write that began exactly at its end.
/// </summary>
[TestClass]
public class BufferStringTests
{
    [TestMethod]
    public void AStringIsUtf8AndTerminated()
    {
        var buffer = BufferManager.CreateBuffer(1, BufferType.Grow, 1);
        BufferManager.WriteBuffer(buffer, "hé", BufferDataType.buffer_string);

        Assert.AreEqual(4, BufferManager.Buffers[buffer].BufferIndex);
        CollectionAssert.AreEqual(new byte[] { 0x68, 0xc3, 0xa9, 0 }, BufferManager.Buffers[buffer].Data[..4]);
    }

    [TestMethod]
    public void TextIsNotTerminated()
    {
        var buffer = BufferManager.CreateBuffer(1, BufferType.Grow, 1);
        BufferManager.WriteBuffer(buffer, "hé", BufferDataType.buffer_text);

        Assert.AreEqual(3, BufferManager.Buffers[buffer].BufferIndex);
    }

    [TestMethod]
    public void TwoStringsReadBackApart()
    {
        var buffer = BufferManager.CreateBuffer(1, BufferType.Grow, 1);
        BufferManager.WriteBuffer(buffer, "one", BufferDataType.buffer_string);
        BufferManager.WriteBuffer(buffer, "twö", BufferDataType.buffer_string);
        BufferManager.Buffers[buffer].BufferIndex = 0;

        Assert.AreEqual("one", BufferManager.ReadBuffer(buffer, BufferDataType.buffer_string));
        Assert.AreEqual("twö", BufferManager.ReadBuffer(buffer, BufferDataType.buffer_string));
    }
}
