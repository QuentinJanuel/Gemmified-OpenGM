using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using StbImageSharp;

namespace OpenGM.Particles
{
    /// <summary>
    /// The fourteen textures behind part_type_shape (pt_shape_pixel .. pt_shape_snow). GameMaker
    /// ships these with the runner rather than in the game's own data, so a game that only ever
    /// calls part_type_shape -- which is most of them, since it is the cheap way to get a puff of
    /// smoke or a burst of stars without drawing anything -- has no sprite for the engine to find.
    ///
    /// The pixels come from the HTML5 runtime's own copies, scripts/particles/IDR_GIF{2..15}.png
    /// (IDR_GIF1 is an unused placeholder, so shape N is file N+2), stored here as bare 64x64 alpha
    /// masks: every one of them is pure white wherever it is visible at all, and the particle's own
    /// colour is what multiplies through. GameMaker Studio 2 redrew the set (the runtime carries it
    /// separately, under scripts/zeusparticles); these are the pre-GMS2 originals, which is what the
    /// GM8/GMS1 games this engine runs were authored against.
    /// </summary>
    public static class ParticleShapes
    {
        public const int ShapeCount = 14;
        private const int ShapeSize = 64;

        /// <summary>
        /// Lazily uploaded, one texture page per shape -- games typically use one or two shapes, so
        /// uploading all fourteen up front would be thirteen wasted pages. Entries are dropped when
        /// their page goes (a reload unbinds every page, see PageManager.UnbindTextures) rather than
        /// on a ParticleManager.Reset, so a game_restart keeps the textures it already has instead
        /// of re-uploading them and leaking the old ids.
        /// </summary>
        private static readonly SpritePageItem?[] _textures = new SpritePageItem?[ShapeCount];

        private static byte[]? _alphaMasks;

        public static SpritePageItem? GetTexture(int shape)
        {
            if (shape < 0 || shape >= ShapeCount)
            {
                // GameMaker treats an out-of-range shape as "nothing to draw" rather than clamping.
                return null;
            }

            var cached = _textures[shape];
            if (cached != null && PageManager.TexturePages.ContainsKey(cached.Page))
            {
                return cached;
            }

            return _textures[shape] = Upload(shape);
        }

        private static SpritePageItem? Upload(int shape)
        {
            var masks = LoadAlphaMasks();
            if (masks == null)
            {
                return null;
            }

            var pixels = new byte[ShapeSize * ShapeSize * 4];
            for (var i = 0; i < ShapeSize * ShapeSize; i++)
            {
                // White everywhere, including the fully transparent texels: with texture
                // interpolation on (gpu_set_texfilter) a black-but-invisible texel would bleed a
                // dark fringe into the edge of every particle.
                pixels[(i * 4) + 0] = 255;
                pixels[(i * 4) + 1] = 255;
                pixels[(i * 4) + 2] = 255;
                pixels[(i * 4) + 3] = masks[(shape * ShapeSize * ShapeSize) + i];
            }

            var page = $"__particle_shape_{shape}";
            PageManager.UploadTexture(page, new ImageResult
            {
                Width = ShapeSize,
                Height = ShapeSize,
                SourceComp = ColorComponents.RedGreenBlueAlpha,
                Comp = ColorComponents.RedGreenBlueAlpha,
                Data = pixels
            });

            return new SpritePageItem
            {
                SourceX = 0,
                SourceY = 0,
                SourceWidth = ShapeSize,
                SourceHeight = ShapeSize,
                TargetX = 0,
                TargetY = 0,
                TargetWidth = ShapeSize,
                TargetHeight = ShapeSize,
                BoundingWidth = ShapeSize,
                BoundingHeight = ShapeSize,
                Page = page
            };
        }

        private static byte[]? LoadAlphaMasks()
        {
            if (_alphaMasks != null)
            {
                return _alphaMasks;
            }

            using var stream = typeof(ParticleShapes).Assembly.GetManifestResourceStream("OpenGM.Particles.particles.dat");
            if (stream == null)
            {
                DebugLog.LogError("Built-in particle shapes are missing from the build.");
                return null;
            }

            var masks = new byte[ShapeCount * ShapeSize * ShapeSize];
            stream.ReadExactly(masks);
            return _alphaMasks = masks;
        }
    }
}
