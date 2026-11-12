using MemoryPack;
using OpenTK.Mathematics;
using UndertaleModLib.Models;

namespace OpenGM.SerializedFiles;

[MemoryPackable]
public partial class SpriteData
{
    public int AssetIndex;
    public string Name = null!;
    public int Width;
    public int Height;
    public int MarginLeft;
    public int MarginRight;
    public int MarginBottom;
    public int MarginTop;
    public int BBoxMode;
    public UndertaleSprite.SepMaskType SepMasks;
    public int OriginX;
    public int OriginY;
    public List<SpritePageItem> Textures = null!;
    public List<byte[]> CollisionMasks = new();
    public float PlaybackSpeed;
    public AnimSpeedType PlaybackSpeedType;

    /// <summary>
    /// Nine-slice setup, or null for the great majority of sprites that have none. See
    /// <see cref="NineSliceData"/>.
    /// </summary>
    public NineSliceData? NineSlice;

    [MemoryPackIgnore] public Vector2i Origin => new(OriginX, OriginY);

    [MemoryPackIgnore] public Vector4i Margins => new(MarginLeft, MarginRight, MarginBottom, MarginTop);
}

/// <summary>
/// A sprite's nine-slice setup: the four margins carve it into nine regions, and each region gets
/// its own behaviour when the sprite is drawn at a size other than its own. GameMaker applies this
/// instead of plain stretching whenever it is enabled, which is the whole point of it -- a UI box
/// keeps its corners crisp and a repeating strip repeats rather than smearing.
/// </summary>
[MemoryPackable]
public partial class NineSliceData
{
    public bool Enabled;
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    /// <summary>
    /// Per-region behaviour, in GameMaker's own order: left edge, top edge, right edge, bottom
    /// edge, centre. Values are <see cref="UndertaleSprite.NineSlice.TileMode"/>.
    /// </summary>
    public int[] TileModes = new int[5];
}

[MemoryPackable]
public partial class SpritePageItem
{
    /// <summary>
    /// YYTPageEntry.x
    /// </summary>
    public int SourceX;
    /// <summary>
    /// YYTPageEntry.y
    /// </summary>
    public int SourceY;
    /// <summary>
    /// YYTPageEntry.w
    /// </summary>
    public int SourceWidth;
    /// <summary>
    /// YYTPageEntry.h
    /// </summary>
    public int SourceHeight;

    /// <summary>
    /// YYTPageEntry.XOffset
    /// </summary>
    public int TargetX;

    /// <summary>
    /// YYTPageEntry.YOffset
    /// </summary>
    public int TargetY;

    /// <summary>
    /// YYTPageEntry.CropWidth
    /// </summary>
    public int TargetWidth;

    /// <summary>
    /// YYTPageEntry.CropHeight
    /// </summary>
    public int TargetHeight;

    /// <summary>
    /// YYTPageEntry.ow
    /// </summary>
    public int BoundingWidth;

    /// <summary>
    /// YYTPageEntry.oh
    /// </summary>
    public int BoundingHeight;

    /// <summary>
    /// YYTPageEntry.tp
    /// </summary>
    public string Page = null!;
}
