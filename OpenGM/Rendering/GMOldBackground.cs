using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.SerializedFiles;
using OpenTK.Mathematics;

namespace OpenGM.Rendering;

public class GMOldBackground
{
    public bool Enabled;
    public bool Foreground;
    public int Definition;
    public Vector2i Position;
    public bool TilingX;
    public bool TilingY;
    public Vector2i Speed;
    public bool Stretch;
    public Vector2 Scale = Vector2.One;

    /// <summary>
    /// background_blend / background_alpha: the colour every tile is multiplied by, defaulting to
    /// untinted white. Games animate this to recolour a room's backdrop without touching the
    /// image (Continuity's NANG rooms cycle it every step).
    /// </summary>
    public int Blend = 0xFFFFFF;

    public double Alpha = 1;

    /// <summary>
    /// Advances the scroll offset by Speed, once per step -- matches real GM applying
    /// hspeed/vspeed to a room background's offset every frame regardless of whether it's tiled.
    /// </summary>
    public void UpdateOffset()
    {
        Position += Speed;
    }

    public void Draw()
    {
        if (!Enabled || Definition == -1)
        {
            return;
        }

        var background = GameLoader.Backgrounds[Definition];
        var sprite = background.Texture;

        if (sprite == null)
        {
            DebugLog.Log(" - sprite is null");
            return;
        }

        // TODO : handle stretch
        // TODO : handle foreground (drawn in the same pass as backgrounds right now -- see the
        // call site in DrawManager.cs)

        var width = sprite.BoundingWidth * Scale.X;
        var height = sprite.BoundingHeight * Scale.Y;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Tiling repeats the background to cover the CURRENT view's visible room-space rect, not
        // just draw the sprite once at its starting Position -- both the starting offset and the
        // tiling extent are relative to GraphicsManager.ViewArea (the current camera/view), matching
        // real GM's own draw_background/draw_sprite_tiled (which is also why this needs to be
        // recomputed every frame: as the view scrolls, both where tiling starts and how far it needs
        // to extend change too).
        var viewArea = GraphicsManager.ViewArea;
        var srcX = (double)viewArea.X;
        var srcY = (double)viewArea.Y;
        var srcW = (double)viewArea.Z;
        var srcH = (double)viewArea.W;

        // The "+ srcH/2" (and "+ srcW/2" for Y) isn't a typo -- it's slack for a rotated view still
        // needing full coverage, matching the reference implementation's own tile_end_x/tile_end_y.
        double? tileEndX = TilingX ? srcX + srcW + (srcH / 2.0) : null;
        double? tileEndY = TilingY ? srcY + srcH + (srcW / 2.0) : null;

        double x = Position.X;
        double y = Position.Y;

        if (tileEndX.HasValue)
        {
            x = CustomMath.Mod(x, width);
            if (x > 0)
            {
                x -= width;
            }
        }

        if (tileEndY.HasValue)
        {
            y = CustomMath.Mod(y, height);
            if (y > 0)
            {
                y -= height;
            }
        }

        var startX = x;

        while (true)
        {
            while (true)
            {
                DrawTile(sprite, x, y, Scale, Blend.ABGRToCol4(Alpha));
                x += width;

                if (tileEndX.HasValue && x < tileEndX.Value)
                {
                    continue;
                }

                break;
            }

            x = startX;
            y += height;

            if (tileEndY.HasValue && y < tileEndY.Value)
            {
                continue;
            }

            break;
        }
    }

    private static void DrawTile(SpritePageItem sprite, double x, double y, Vector2 scale, Color4 blend)
    {
        CustomWindow.Draw(new GMSpriteJob()
        {
            texture = sprite,
            screenPos = new Vector2d(x, y),
            Colors = [blend, blend, blend, blend],
            angle = 0,
            origin = Vector2.Zero,
            scale = new Vector2d(scale.X, scale.Y)
        });
    }
}
