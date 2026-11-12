using OpenGM.IO;
using OpenGM.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Collections;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class _3DFunctions
    {
        // The three matrices the shader draws with. Identity rather than Matrix4's own all-zero
        // default, or every draw would degenerate to nothing the moment this class is touched, even
        // for a game that never calls a single d3d_transform_*/matrix_set function.
        //
        // They are PROPERTIES so that writing one uploads it -- every d3d_transform_*, every
        // matrix_set, every matrix stack pop, the model translation in D3DModelFunctions. Making
        // that the setter's job is what stops the next one of those from being written and
        // forgotten: they used to be plain fields that nothing on the draw path ever read (see
        // GraphicsManager.UploadMatrices), so the whole family drew as if it had never been called.
        //
        // GraphicsManager.SetViewArea writes the *Field members directly and uploads once itself.
        internal static Matrix4 ViewMatrixField = Matrix4.Identity;
        internal static Matrix4 ProjectionMatrixField = Matrix4.Identity;
        internal static Matrix4 WorldMatrixField = Matrix4.Identity;

        public static Matrix4 ViewMatrix
        {
            get => ViewMatrixField;
            set { ViewMatrixField = value; GraphicsManager.UploadMatrices(); }
        }

        public static Matrix4 ProjectionMatrix
        {
            get => ProjectionMatrixField;
            set { ProjectionMatrixField = value; GraphicsManager.UploadMatrices(); }
        }

        public static Matrix4 WorldMatrix
        {
            get => WorldMatrixField;
            set { WorldMatrixField = value; GraphicsManager.UploadMatrices(); }
        }

        [GMLFunction("d3d_start")]
        public static object? d3d_start(object?[] args)
        {
            // no real 3D pipeline (2D-only renderer) - just needs to not crash D&D/legacy games that call it
            return null;
        }

        [GMLFunction("d3d_end")]
        public static object? d3d_end(object?[] args)
        {
            return null;
        }

        // The d3d_transform_* family manipulates the same WorldMatrix matrix_set/matrix_get expose
        // (matrix type 2) -- ShaderManager.LoadMatrices feeds it into gm_Matrices, which the vertex
        // shader applies to every 2D draw too (not just real 3D geometry), so GM8/GMS games often
        // use it as the only way to rotate/scale draw calls that don't take their own angle
        // argument (e.g. draw_sprite_part_ext, wrapped by this game's own draw_sprite_part_ext_rot).
        //
        // "add_*" composes onto the current transform in call order -- WorldMatrix * new, matching
        // the same row-vector chaining ShaderManager.LoadMatrices already uses for world * view *
        // projection -- so e.g. add_translation(-px,-py) then add_rotation_z(a) then
        // add_translation(px,py) rotates around (px,py), applied in that exact sequence.

        [GMLFunction("d3d_transform_set_identity")]
        public static object? d3d_transform_set_identity(object?[] args)
        {
            WorldMatrix = Matrix4.Identity;
            return null;
        }

        [GMLFunction("d3d_transform_set_translation")]
        public static object? d3d_transform_set_translation(object?[] args)
        {
            WorldMatrix = Matrix4.CreateTranslation(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            return null;
        }

        [GMLFunction("d3d_transform_set_scaling")]
        public static object? d3d_transform_set_scaling(object?[] args)
        {
            WorldMatrix = Matrix4.CreateScale(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            return null;
        }

        // Real GM's rotation direction/units match image_angle (degrees, counter-clockwise) -- see
        // CustomMath.RotateAroundPoint's own "-angle" for the same screen-space (Y-down) conversion.
        [GMLFunction("d3d_transform_set_rotation_x")]
        public static object? d3d_transform_set_rotation_x(object?[] args)
        {
            WorldMatrix = Matrix4.CreateRotationX((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_set_rotation_y")]
        public static object? d3d_transform_set_rotation_y(object?[] args)
        {
            WorldMatrix = Matrix4.CreateRotationY((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_set_rotation_z")]
        public static object? d3d_transform_set_rotation_z(object?[] args)
        {
            WorldMatrix = Matrix4.CreateRotationZ((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_set_rotation_axis")]
        public static object? d3d_transform_set_rotation_axis(object?[] args)
        {
            var axis = new Vector3(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            WorldMatrix = Matrix4.CreateFromAxisAngle(axis, (float)(-args[3].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_add_translation")]
        public static object? d3d_transform_add_translation(object?[] args)
        {
            WorldMatrix *= Matrix4.CreateTranslation(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            return null;
        }

        [GMLFunction("d3d_transform_add_scaling")]
        public static object? d3d_transform_add_scaling(object?[] args)
        {
            WorldMatrix *= Matrix4.CreateScale(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            return null;
        }

        [GMLFunction("d3d_transform_add_rotation_x")]
        public static object? d3d_transform_add_rotation_x(object?[] args)
        {
            WorldMatrix *= Matrix4.CreateRotationX((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_add_rotation_y")]
        public static object? d3d_transform_add_rotation_y(object?[] args)
        {
            WorldMatrix *= Matrix4.CreateRotationY((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_add_rotation_z")]
        public static object? d3d_transform_add_rotation_z(object?[] args)
        {
            WorldMatrix *= Matrix4.CreateRotationZ((float)(-args[0].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        [GMLFunction("d3d_transform_add_rotation_axis")]
        public static object? d3d_transform_add_rotation_axis(object?[] args)
        {
            var axis = new Vector3(args[0].Conv<float>(), args[1].Conv<float>(), args[2].Conv<float>());
            WorldMatrix *= Matrix4.CreateFromAxisAngle(axis, (float)(-args[3].Conv<double>() * CustomMath.Deg2Rad));
            return null;
        }

        // The transform stack (push/pop/etc) isn't implemented -- no game in this codebase's test
        // corpus so far uses it (only set_identity/add_translation/add_rotation_z, for one-shot
        // pivot rotation around a single draw call), so these are safe no-ops rather than a real
        // stack for now.
        [GMLFunction("d3d_transform_stack_clear", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_clear(object?[] args) => null;

        [GMLFunction("d3d_transform_stack_empty", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_empty(object?[] args) => true;

        [GMLFunction("d3d_transform_stack_push", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_push(object?[] args) => false;

        [GMLFunction("d3d_transform_stack_pop", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_pop(object?[] args) => false;

        [GMLFunction("d3d_transform_stack_top", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_top(object?[] args) => false;

        [GMLFunction("d3d_transform_stack_discard", GMLFunctionFlags.Stub)]
        public static object? d3d_transform_stack_discard(object?[] args) => false;

        private static Matrix4 ListToMatrix(IList list)
        {
            if (list.Count != 16)
            {
                throw new Exception("Array must contain exactly 16 elements");
            }

            var matrix = new Matrix4();
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    matrix[i, j] = list[(i * 4) + j].Conv<float>();
                }
            }

            return matrix;
        }

        private static IList MatrixToList(Matrix4 matrix)
        {
            var array = new double[16];

            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    array[(i * 4) + j] = matrix[i, j];
                }
            }

            return array;
        }

        /// <summary>Exposed for MatrixStackFunctions, which shares this representation.</summary>
        public static object? MatrixToListPublic(Matrix4 m) => MatrixToList(m);

        public static Matrix4 ListToMatrixPublic(IList list) => ListToMatrix(list);

        [GMLFunction("matrix_get")]
        public static object? matrix_get(object?[] args)
        {
            var type = args[0].Conv<int>();

            return type switch
            {
                0 => MatrixToList(ViewMatrix),
                1 => MatrixToList(ProjectionMatrix),
                2 => MatrixToList(WorldMatrix),
                _ => throw new Exception("Illegal matrix type"),
            };
        }

        [GMLFunction("matrix_set")]
        public static object? matrix_set(object?[] args)
        {
            var type = args[0].Conv<int>();
            var value = ListToMatrix(args[1].Conv<IList>());

            switch (type)
            {
                case 0:
                    ViewMatrix = value;
                    break;
                case 1:
                    ProjectionMatrix = value;
                    break;
                case 2:
                    WorldMatrix = value;
                    break;
                default:
                    throw new Exception("Illegal matrix type");
            }

            return null;
        }

        [GMLFunction("matrix_build_identity")]
        public static object? matrix_build_identity(object?[] args)
        {
            return MatrixToList(Matrix4.Identity);
        }

        [GMLFunction("matrix_build")]
        public static object? matrix_build(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();
            var z = args[2].Conv<double>();
            var xrot = args[3].Conv<double>();
            var yrot = args[4].Conv<double>();
            var zrot = args[5].Conv<double>();
            var xscale = args[6].Conv<double>();
            var yscale = args[7].Conv<double>();
            var zscale = args[8].Conv<double>();

            // TODO : there is probably a built in function for this, but i forgor
            // https://github.com/YoYoGames/GameMaker-HTML5/blob/3560546dc80cb3c0ec4627021d720a08fc58a95f/scripts/functions/Function_D3D.js#L839

            xrot = -xrot * CustomMath.Deg2Rad;
            yrot = -yrot * CustomMath.Deg2Rad;
            zrot = -zrot * CustomMath.Deg2Rad;

            var sinx = Math.Sin(xrot);
            var cosx = Math.Cos(xrot);
            var siny = Math.Sin(yrot);
            var cosy = Math.Cos(yrot);
            var sinz = Math.Sin(zrot);
            var cosz = Math.Cos(zrot);

            var sinzsinx = -sinz * -sinx;
            var coszsinx = cosz * -sinx;

            var ret = new double[16];

            ret[0] = ((cosz * cosy) + (sinzsinx * -siny)) * xscale;
            ret[4] = -sinz * cosx * xscale;
            ret[8] = ((cosz * siny) + (sinzsinx * cosy)) * xscale;
            ret[12] = x;

            ret[1] = ((sinz * cosy) + (coszsinx * -siny)) * yscale;
            ret[5] = cosz * cosx * yscale;
            ret[9] = ((sinz * siny) + (coszsinx * cosy)) * yscale;
            ret[13] = y;

            ret[2] = cosx * -siny * zscale;
            ret[6] = sinx * zscale;
            ret[10] = cosx * cosy * zscale;
            ret[14] = z;

            ret[3] = ret[7] = ret[11] = 0.0;
            ret[15] = 1.0;

            return ret;
        }

        [GMLFunction("matrix_build_lookat")]
        public static object? matrix_build_lookat(object?[] args)
        {
            var xfrom = (float)args[0].Conv<double>();
            var yfrom = (float)args[1].Conv<double>();
            var zfrom = (float)args[2].Conv<double>();
            var xto = (float)args[3].Conv<double>();
            var yto = (float)args[4].Conv<double>();
            var zto = (float)args[5].Conv<double>();
            var xup = (float)args[6].Conv<double>();
            var yup = (float)args[7].Conv<double>();
            var zup = (float)args[8].Conv<double>();

            return MatrixToList(Matrix4.LookAt(xfrom, yfrom, zfrom, xto, yto, zto, xup, yup, zup));
        }

        [GMLFunction("matrix_build_projection_ortho")]
        public static object? matrix_build_projection_ortho(object?[] args)
        {
            var w = (float)args[0].Conv<double>();
            var h = (float)args[1].Conv<double>();
            var znear = (float)args[2].Conv<double>();
            var zfar = (float)args[3].Conv<double>();

            return MatrixToList(Matrix4.CreateOrthographic(w, h, znear, zfar));
        }

        [GMLFunction("matrix_build_projection_perspective")]
        public static object? matrix_build_projection_perspective(object?[] args)
        {
            var w = (float)args[0].Conv<double>();
            var h = (float)args[1].Conv<double>();
            var znear = (float)args[2].Conv<double>();
            var zfar = (float)args[3].Conv<double>();

            // https://github.com/YoYoGames/GameMaker-HTML5/blob/3560546dc80cb3c0ec4627021d720a08fc58a95f/scripts/Matrix.js#L240

            // TODO - built in function for this?
            // calculate FOV from w/h and near plane?
            // or just easier to do this?

            return new double[16]
            {
                (znear + znear) / w, 0, 0, 0,
                0, (znear + znear) / h, 0, 0,
                0, 0, zfar / (zfar - znear), 1,
                0, 0, -znear * zfar / (zfar - znear), 0
            };
        }

        [GMLFunction("matrix_build_projection_perspective_fov")]
        public static object? matrix_build_projection_perspective_fov(object?[] args)
        {
            var fov = args[0].Conv<double>();
            var aspect = args[1].Conv<double>();
            var znear = (float)args[2].Conv<double>();
            var zfar = (float)args[3].Conv<double>();

            // https://github.com/YoYoGames/GameMaker-HTML5/blob/3560546dc80cb3c0ec4627021d720a08fc58a95f/scripts/Matrix.js#L200

            fov *= CustomMath.Deg2Rad;
            var yScale = 1 / Math.Tan(fov / 2);
            var xScale = yScale / aspect;

            return new double[16]
            {
                xScale, 0, 0, 0,
                0, yScale, 0, 0,
                0, 0, zfar / (zfar - znear), 1,
                0, 0, -znear * zfar / (zfar - znear), 0
            };
        }

        [GMLFunction("matrix_multiply")]
        public static object? matrix_multiply(object?[] args)
        {
            var matrix1 = ListToMatrix(args[0].Conv<IList>());
            var matrix2 = ListToMatrix(args[1].Conv<IList>());

            return MatrixToList(Matrix4.Mult(matrix1, matrix2));
        }

        [GMLFunction("matrix_transform_vertex")]
        public static object? matrix_transform_vertex(object?[] args)
        {
            var matrix = ListToMatrix(args[0].Conv<IList>());
            var x = (float)args[1].Conv<double>();
            var y = (float)args[2].Conv<double>();
            var z = (float)args[3].Conv<double>();

            var vector = new Vector4(x, y, z, 1); // TODO : double check this is meant to be 1 bc im tired when writing this
            var ret = vector * matrix;
            return new double[] { ret.X, ret.Y, ret.Z };
        }

        // draw_texture_flush

        [GMLFunction("draw_flush", GMLFunctionFlags.Stub)]
        public static object? draw_flush(object?[] args)
        {
            return null;
        }

        // matrix_stack_push
        // matrix_stack_pop
        // matrix_stack_set
        // matrix_stack_clear
        // matrix_stack_top
        // matrix_stack_is_empty

        [GMLFunction("gpu_set_blendenable")]
        // The pre-Studio name for the same thing: turn alpha blending off and what's drawn
        // overwrites the target's own alpha instead of compositing onto it, which is how a game
        // punches a shape's alpha into a surface.
        [GMLFunction("draw_enable_alphablend", before: "2.0.0.0")]
        public static object? gpu_set_blendenable(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var enable = args[0].Conv<bool>();

            // TODO : is this right?

            if (enable)
            {
                GL.Enable(EnableCap.Blend);
            }
            else
            {
                GL.Disable(EnableCap.Blend);
            }

            return null;
        }

        // gpu_set_ztestenable
        // gpu_set_zfunc
        // gpu_set_zwriteenable

        [GMLFunction("gpu_set_fog")]
        public static object? gpu_set_fog(object?[] args)
        {
            var enable = args[0].Conv<bool>();
            var colour = args[1].Conv<int>();
            var start = args[2].Conv<float>();
            var end = args[3].Conv<float>();
            GpuStateFunctions.Current.FogEnabled = enable;
            GpuStateFunctions.Current.FogColour = colour;
            GpuStateFunctions.Current.FogStart = start;
            GpuStateFunctions.Current.FogEnd = end;

            // Opaque, explicitly. A GML colour is 24 bits of BGR with nothing in the top byte, and
            // the no-alpha ABGRToCol4 overload reads that empty byte as alpha -- so every fog colour
            // arrived as alpha 0. The shader's DoFog is `mix(SrcColour, gm_FogColour, fogval)`, alpha
            // included, so fully-fogged geometry came out completely transparent instead of the fog
            // colour: it was drawn, blended to nothing, and vanished.
            //
            // tijit engine 2 renders its blocks and spikes by drawing them fully fogged into a
            // surface (fog range (-1, 1)) and keying on that surface's red channel, so this is the
            // difference between a room's whole tileset being there and being invisible.
            GraphicsManager.SetFog(enable, colour.ABGRToCol4(1.0), start, end);

            return null;
        }

        // gpu_set_cullmode

        [GMLFunction("gpu_set_sprite_cull")]
        public static object? gpu_set_sprite_cull(object?[] args)
        {
            var enabled = args[0].Conv<bool>();

            GraphicsManager.EnableCulling = enabled;

            return null;
        }

        public static int Blendmode;

        [GMLFunction("gpu_set_blendmode")]
        public static object? gpu_set_blendmode(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var mode = args[0].Conv<int>();
            GpuStateFunctions.Current.BlendMode = mode;
            Blendmode = mode;

            switch (mode)
            {
                case 0:
                    // bm_normal
                    GraphicsManager.PushMessage("BM_NORMAL");
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;
                case 1:
                    // bm_add
                    GraphicsManager.PushMessage("BM_ADD");
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;
                case 2:
                    // bm_max
                    GraphicsManager.PushMessage("BM_MAX");
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcColor);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;
                case 3:
                    // bm_subtract
                    GraphicsManager.PushMessage("BM_SUBTRACT");
                    // https://github.com/YoYoGames/GameMaker-Bugs/issues/11061#issuecomment-3005485747
                    GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.OneMinusSrcColor);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;
                case 4:
                    // "bm_min" -- the name is UndertaleModLib's (BuiltinList.cs maps the constant 4
                    // to it), not GameMaker's. GameMaker documents exactly four blend modes, all of
                    // them plain factor pairs under the ADD equation: bm_normal, bm_add, bm_max and
                    // bm_subtract, i.e. 0..3. 4 is past the end of that table, and everything about
                    // how the only game to use it renders says GameMaker just leaves ordinary alpha
                    // blending in place rather than selecting some fifth mode.
                    //
                    // Treating it as a genuine minimum blend cannot be right. I Wanna Spook Jam's
                    // WetWookie lanterns are the whole corpus's only use of it (4 calls, both in
                    // objWookLight/objWookLightControl). They build a lantern glow the ordinary way:
                    // a white radial gradient (sprWookLight -- white throughout, alpha 253 at the
                    // centre falling to ~2 at the rim) at image_alpha 0.6, then a *yellow* copy at
                    // 0.5 and 0.8x scale over it. Layered through normal alpha compositing that is a
                    // white-hot core with a warm yellow fringe, which is exactly how the fight looks
                    // on Windows. Under any min-based blend it cannot look like that at all: the
                    // destination those are drawn onto is the black darkness overlay, and min against
                    // black stays black, so the yellow pass contributes literally nothing and the
                    // "light" comes out unlit.
                    //
                    // Two wrong versions preceded this one, both worth recording. BlendEquation(Min)
                    // (a) ignores the blend factors entirely in OpenGL -- unlike D3D9, which is what
                    // GMS1.4 renders through -- collapsing to a per-channel min(src, dst), which
                    // settles the overlay at min(0.3, texA * 0.6): the lantern's centre stays exactly
                    // as dark as no lantern at all, and the darkness only lifts below texA = 0.5, a
                    // razor-sharp circular threshold at the half-alpha radius, drawn as a hard-edged
                    // dark disc. Destination-out (Zero, OneMinusSrcAlpha) fixed the disc but zeroes
                    // the source colour, so the yellow pass still contributed nothing -- and on the
                    // screen-space copy of the same draw (objWookLight's own Draw event) it scaled
                    // the scene down by (1 - srcA), turning the glow into a black smudge.
                    GraphicsManager.PushMessage("BM_MIN");
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;
                case 5:
                    // bm_reverse_subtract
                    GraphicsManager.PushMessage("BM_REVERSE_SUBTRACT");
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                    // TODO: had a look in RenderDoc and reverse_subtract is not doing what the docs say. fun.
                    //GL.BlendEquation(BlendEquationMode.FuncReverseSubtract);
                    GL.BlendEquation(BlendEquationMode.FuncSubtract);
                    break;
            }

            GraphicsManager.PopMessage();
            return null;
        }

        [GMLFunction("gpu_set_blendmode_ext")]
        public static object? gpu_set_blendmode_ext(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var src = args[0].Conv<int>();
            var dst = args[1].Conv<int>();
            // Recorded so gpu_get_blendmode_src/_dest can read it back -- the save/restore idiom
            // depends on the getters, and a getter that answers a default silently resets state.
            GpuStateFunctions.Current.BlendSrc = src;
            GpuStateFunctions.Current.BlendDest = dst;
            GpuStateFunctions.Current.BlendSrcAlpha = src;
            GpuStateFunctions.Current.BlendDestAlpha = dst;

            BlendingFactor GetBlend(int arg) => arg switch
            {
                1 => BlendingFactor.Zero,
                2 => BlendingFactor.One,
                3 => BlendingFactor.SrcColor,
                4 => BlendingFactor.OneMinusSrcColor,
                5 => BlendingFactor.SrcAlpha,
                6 => BlendingFactor.OneMinusSrcAlpha,
                7 => BlendingFactor.DstAlpha,
                8 => BlendingFactor.OneMinusDstAlpha,
                9 => BlendingFactor.DstColor,
                10 => BlendingFactor.OneMinusDstColor,
                11 => BlendingFactor.SrcAlphaSaturate,
                _ => throw new ArgumentException(),
            };

            GL.BlendFunc(GetBlend(src), GetBlend(dst));
            // gpu_set_blendmode sets an equation as well as a function (Min for bm_min, Subtract for
            // bm_subtract/bm_reverse_subtract). This entry point only ever set the function, so a
            // game that used one of those and then switched to an _ext mode kept the old equation
            // and every later draw was still min'd/subtracted. The _ext modes are all plain
            // src*sf + dst*df, so the equation always goes back to Add.
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            return null;
        }

        [GMLFunction("gpu_set_blendmode_ext_sepalpha")]
        public static object? gpu_set_blendmode_ext_sepalpha(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var src = args[0].Conv<int>();
            var dest = args[1].Conv<int>();
            var alphasrc = args[2].Conv<int>();
            GpuStateFunctions.Current.BlendSrc = src;
            GpuStateFunctions.Current.BlendDest = dest;
            GpuStateFunctions.Current.BlendSrcAlpha = alphasrc;
            var alphadest = args[3].Conv<int>();

            // TODO : theres gotta be a better way then repeating all these switch cases

            BlendingFactorSrc GetBlendSrc(int arg) => arg switch
            {
                1 => BlendingFactorSrc.Zero,
                2 => BlendingFactorSrc.One,
                3 => BlendingFactorSrc.SrcColor,
                4 => BlendingFactorSrc.OneMinusSrcColor,
                5 => BlendingFactorSrc.SrcAlpha,
                6 => BlendingFactorSrc.OneMinusSrcAlpha,
                7 => BlendingFactorSrc.DstAlpha,
                8 => BlendingFactorSrc.OneMinusDstAlpha,
                9 => BlendingFactorSrc.DstColor,
                10 => BlendingFactorSrc.OneMinusDstColor,
                11 => BlendingFactorSrc.SrcAlphaSaturate,
                _ => throw new ArgumentException(),
            };

            BlendingFactorDest GetBlendDest(int arg) => arg switch
            {
                1 => BlendingFactorDest.Zero,
                2 => BlendingFactorDest.One,
                3 => BlendingFactorDest.SrcColor,
                4 => BlendingFactorDest.OneMinusSrcColor,
                5 => BlendingFactorDest.SrcAlpha,
                6 => BlendingFactorDest.OneMinusSrcAlpha,
                7 => BlendingFactorDest.DstAlpha,
                8 => BlendingFactorDest.OneMinusDstAlpha,
                9 => BlendingFactorDest.DstColor,
                10 => BlendingFactorDest.OneMinusDstColor,
                11 => BlendingFactorDest.SrcAlphaSaturate,
                _ => throw new ArgumentException(),
            };

            // Was GetBlendSrc(src), GetBlendDest(src), GetBlendSrc(src), GetBlendDest(src) -- `src`
            // four times, so `dest`, `alphasrc` and `alphadest` were all silently discarded and the
            // colour pair was (src, src). The whole point of the sepalpha variant is the other three.
            GL.BlendFuncSeparate(GetBlendSrc(src), GetBlendDest(dest), GetBlendSrc(alphasrc), GetBlendDest(alphadest));
            GL.BlendEquation(BlendEquationMode.FuncAdd); // same reasoning as gpu_set_blendmode_ext
            return null;
        }

        [GMLFunction("gpu_set_colorwriteenable")]
        [GMLFunction("gpu_set_colourwriteenable")]
        public static object? gpu_set_colourwriteenable(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            bool r;
            bool g;
            bool b;
            bool a;

            if (args.Length == 4)
            {
                r = args[0].Conv<bool>();
                g = args[1].Conv<bool>();
                b = args[2].Conv<bool>();
                a = args[3].Conv<bool>();
            }
            else
            {
                var array = args[0].Conv<IList>();
                r = array[0].Conv<bool>();
                g = array[1].Conv<bool>();
                b = array[2].Conv<bool>();
                a = array[3].Conv<bool>();
            }

            GL.ColorMask(r, g, b, a);
            return null;
        }

        [GMLFunction("gpu_set_alphatestenable")]
        public static object? gpu_set_alphatestenable(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var enabled = args[0].Conv<bool>();
            GpuStateFunctions.Current.AlphaTestEnable = enabled;
            GL.Uniform1(ShaderManager.gm_AlphaTestEnabled, enabled ? 1 : 0);
            return null;
        }

        [GMLFunction("gpu_set_alphatestref")]
        public static object? gpu_set_alphatestref(object?[] args)
        {
            GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD GPU state
            var alphaRef = args[0].Conv<int>();
            GpuStateFunctions.Current.AlphaTestRef = alphaRef;
            GL.Uniform1(ShaderManager.gm_AlphaRefValue, alphaRef / 255f);
            return null;
        }

        [GMLFunction("d3d_set_fog")]
        public static object? d3d_set_fog(object?[] args) => gpu_set_fog(args);

        [GMLFunction("texture_set_interpolation")]
        public static object? texture_set_interpolation(object?[] args)
        {
            var enable = args[0].Conv<bool>();
            PageManager.SetInterpolation(enable);
            return null;
        }

        [GMLFunction("texture_get_interpolation")]
        public static object texture_get_interpolation(object?[] args)
        {
            return PageManager.TextureInterpolation;
        }

        // gpu_set_texrepeat_ext
        // gpu_set_tex_filter
        // gpu_set_tex_filter_ext
        // gpu_set_tex_repeat
        // gpu_set_tex_repeat_ext
        // gpu_set_tex_mip_filter
        // gpu_set_tex_mip_filter_ext
        // gpu_set_tex_mip_bias
        // gpu_set_tex_mip_bias_ext
        // gpu_set_tex_min_mip
        // gpu_set_tex_min_mip_ext
        // gpu_set_tex_max_mip
        // gpu_set_tex_max_mip_ext
        // gpu_set_tex_max_aniso
        // gpu_set_tex_max_aniso_ext
        // gpu_set_tex_mip_enable
        // gpu_set_tex_mip_enable_ext

        [GMLFunction("gpu_get_blendenable")]
        public static object? gpu_get_blendenable(object?[] args)
        {
            return GL.GetBoolean(GetPName.Blend); // https://registry.khronos.org/OpenGL-Refpages/gl4/html/glBlendFunc.xhtml
        }

        // gpu_get_ztestenable
        // gpu_get_zfunc
        // gpu_get_zwriteenable
        // gpu_get_fog
        // gpu_get_cullmode

        [GMLFunction("gpu_get_blendmode")]
        public static object? gpu_get_blendmode(object?[] args)
        {
            return Blendmode;
        }
        // gpu_get_blendmode_ext_sepalpha
        // gpu_get_blendmode_src
        // gpu_get_blendmode_dest
        // gpu_get_blendmode_srcalpha
        // gpu_get_blendmode_destalpha

        [GMLFunction("gpu_get_colorwriteenable")]
        [GMLFunction("gpu_get_colourwriteenable")]
        public static object? gpu_get_colourwriteenable(object?[] args)
        {
            var bools = new bool[4];
            GL.GetBoolean(GetPName.ColorWritemask, bools); // https://registry.khronos.org/OpenGL-Refpages/gl4/html/glColorMask.xhtml
            return bools;
        }

        // gpu_get_alphatestenable
        // gpu_get_alphatestref
        // gpu_get_texfilter
        // gpu_get_texfilter_ext
        // gpu_get_texrepeat
        // gpu_get_texrepeat_ext
        // gpu_get_tex_filter
        // gpu_get_tex_filter_ext
        // gpu_get_tex_repeat
        // gpu_get_tex_repeat_ext
        // gpu_get_tex_mip_filter
        // gpu_get_tex_mip_filter_ext
        // gpu_get_tex_mip_bias
        // gpu_get_tex_mip_bias_ext
        // gpu_get_tex_min_mip
        // gpu_get_tex_min_mip_ext
        // gpu_get_tex_max_mip
        // gpu_get_tex_max_mip_ext
        // gpu_get_tex_max_aniso
        // gpu_get_tex_max_aniso_ext
        // gpu_get_tex_mip_enable
        // gpu_get_tex_mip_enable_ext
        // gpu_push_state
        // gpu_pop_state
        // gpu_get_state
        // gpu_set_state
        // draw_light_define_ambient
        // draw_light_define_direction
        // draw_light_define_point
        // draw_light_enable
        // draw_set_lighting
        // draw_light_get_ambient
        // draw_light_get
        // draw_get_lighting
    }
}
