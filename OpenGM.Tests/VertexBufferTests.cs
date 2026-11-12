using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Mathematics;

namespace OpenGM.Tests;

/// <summary>
/// The vertex buffer family (VertexBufferFunctions). A GMS game that builds its world as a mesh
/// draws nothing whatsoever without these -- a missing function aborts the whole calling event, so
/// tijit engine 2's world object never finished its room-start and the screen stayed black.
///
/// vertex_submit needs a GL context, so what's covered here is the part that doesn't: the element
/// cursor that decides when a vertex is complete, which is the only real logic in the family and the
/// thing that silently produces a mesh of the wrong shape (or no mesh at all) when it's wrong.
/// </summary>
[TestClass]
public class VertexBufferTests
{
    [TestInitialize]
    public void SetUp() => VertexBufferFunctions.DiscardBuffers();

    [TestCleanup]
    public void TearDown() => VertexBufferFunctions.DiscardBuffers();

    /// <summary>position + colour, which is what a game's untextured mesh format looks like.</summary>
    private static int PositionColourFormat()
    {
        VertexFormatFunctions.vertex_format_begin([]);
        VertexFormatFunctions.vertex_format_add_position([]);
        VertexFormatFunctions.vertex_format_add_colour([]);
        return VertexFormatFunctions.vertex_format_end([])!.Conv<int>();
    }

    /// <summary>position + texcoord + colour, i.e. tijit engine 2's autotile format.</summary>
    private static int PositionTexcoordColourFormat()
    {
        VertexFormatFunctions.vertex_format_begin([]);
        VertexFormatFunctions.vertex_format_add_position([]);
        VertexFormatFunctions.vertex_format_add_texcoord([]);
        VertexFormatFunctions.vertex_format_add_colour([]);
        return VertexFormatFunctions.vertex_format_end([])!.Conv<int>();
    }

    private static int NewBuffer() => VertexBufferFunctions.vertex_create_buffer([])!.Conv<int>();

    [TestMethod]
    public void FormatIdsAreUsableAndNonZero()
    {
        // 0 is false in GML, so a format id of 0 would make `if (global.format)` take the wrong
        // branch in a game that guards on it.
        var first = PositionColourFormat();
        var second = PositionColourFormat();

        Assert.AreNotEqual(0, first);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void AVertexIsCompleteOnlyWhenEveryAttributeIsWritten()
    {
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        VertexBufferFunctions.vertex_position([buffer, 10.0, 20.0]);

        // Position alone is half a vertex: the colour hasn't been written, so nothing is committed.
        Assert.AreEqual(0, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());

        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        Assert.AreEqual(1, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());
    }

    [TestMethod]
    public void ATriangleKeepsItsPositionsColoursAndUvs()
    {
        var format = PositionTexcoordColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        for (var i = 0; i < 3; i++)
        {
            VertexBufferFunctions.vertex_position([buffer, (double)i, i * 2.0]);
            VertexBufferFunctions.vertex_texcoord([buffer, i * 0.25, i * 0.5]);
            // c_red is 0x0000FF in GML's BGR packing, so this is fully red, half transparent.
            VertexBufferFunctions.vertex_colour([buffer, 255, 0.5]);
        }
        VertexBufferFunctions.vertex_end([buffer]);

        var vertices = VertexBufferFunctions.PeekBuffer(buffer);
        Assert.AreEqual(3, vertices.Length);

        for (var i = 0; i < 3; i++)
        {
            Assert.AreEqual(i, vertices[i].pos.X, 0.0001f, $"vertex {i} x");
            Assert.AreEqual(i * 2f, vertices[i].pos.Y, 0.0001f, $"vertex {i} y");
            // 2D positions are z = 0, NOT the current GR_Depth: a buffer is submitted long after
            // the instance whose depth that happens to hold.
            Assert.AreEqual(0f, vertices[i].pos.Z, 0.0001f, $"vertex {i} z");

            // Texture coordinates go through untouched -- no sprite sub-rect mapping, because in
            // GMS a texture IS the whole page and a game passes page coordinates (sprite_get_uvs).
            Assert.AreEqual(i * 0.25f, vertices[i].uv.X, 0.0001f, $"vertex {i} u");
            Assert.AreEqual(i * 0.5f, vertices[i].uv.Y, 0.0001f, $"vertex {i} v");

            Assert.AreEqual(1f, vertices[i].color.X, 0.0001f, $"vertex {i} red");
            Assert.AreEqual(0f, vertices[i].color.Y, 0.0001f, $"vertex {i} green");
            Assert.AreEqual(0f, vertices[i].color.Z, 0.0001f, $"vertex {i} blue");
            // 127/255, not exactly 0.5: ABGRToCol4 quantises alpha to a byte the way GM stores it.
            Assert.AreEqual(127f / 255f, vertices[i].color.W, 0.0001f, $"vertex {i} alpha");
        }
    }

    [TestMethod]
    public void AnAttributeTheGameNeverWritesKeepsItsDefault()
    {
        // A position-and-colour format written with position only never completes a vertex, which
        // is the honest outcome: real GM would keep writing bytes and mis-align everything after.
        // What this pins is the opposite case -- the format is fully written, so the vertex is
        // committed, and the attribute the format doesn't have (uv) is left at its default.
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        VertexBufferFunctions.vertex_position([buffer, 5.0, 6.0]);
        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        VertexBufferFunctions.vertex_end([buffer]);

        var vertices = VertexBufferFunctions.PeekBuffer(buffer);
        Assert.AreEqual(1, vertices.Length);
        Assert.AreEqual(Vector2.Zero, vertices[0].uv);
    }

    [TestMethod]
    public void VertexBeginStartsTheBufferOver()
    {
        // A game rebuilds its world mesh on every room start, into the same buffer.
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        VertexBufferFunctions.vertex_position([buffer, 0.0, 0.0]);
        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        VertexBufferFunctions.vertex_end([buffer]);
        Assert.AreEqual(1, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());

        VertexBufferFunctions.vertex_begin([buffer, format]);
        Assert.AreEqual(0, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());
    }

    [TestMethod]
    public void WritesOutsideVertexBeginAreIgnoredRatherThanCrashing()
    {
        var buffer = NewBuffer();

        // No vertex_begin: there is no format to write against. This must not throw -- an exception
        // out of a GML built-in aborts the calling event, which is exactly the failure mode this
        // whole family exists to remove.
        VertexBufferFunctions.vertex_position([buffer, 1.0, 2.0]);
        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);

        Assert.AreEqual(0, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());
    }

    [TestMethod]
    public void APartiallyWrittenVertexIsDroppedAtVertexEnd()
    {
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        VertexBufferFunctions.vertex_position([buffer, 1.0, 2.0]);
        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        VertexBufferFunctions.vertex_position([buffer, 3.0, 4.0]); // no colour follows
        VertexBufferFunctions.vertex_end([buffer]);

        Assert.AreEqual(1, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());
    }

    [TestMethod]
    public void ADeletedBufferReportsNothingAndIsSafeToUse()
    {
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        VertexBufferFunctions.vertex_position([buffer, 1.0, 2.0]);
        VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        VertexBufferFunctions.vertex_end([buffer]);
        VertexBufferFunctions.vertex_delete_buffer([buffer]);

        Assert.AreEqual(0, VertexBufferFunctions.vertex_get_number([buffer])!.Conv<int>());
        VertexBufferFunctions.vertex_begin([buffer, format]);
    }

    [TestMethod]
    public void BufferSizeIsVerticesTimesTheFormatStride()
    {
        // position (2 floats) + colour (4 bytes) = 12 bytes a vertex.
        var format = PositionColourFormat();
        var buffer = NewBuffer();

        VertexBufferFunctions.vertex_begin([buffer, format]);
        for (var i = 0; i < 4; i++)
        {
            VertexBufferFunctions.vertex_position([buffer, (double)i, 0.0]);
            VertexBufferFunctions.vertex_colour([buffer, 16777215, 1.0]);
        }
        VertexBufferFunctions.vertex_end([buffer]);

        Assert.AreEqual(48, VertexBufferFunctions.vertex_get_buffer_size([buffer])!.Conv<int>());
    }
}
