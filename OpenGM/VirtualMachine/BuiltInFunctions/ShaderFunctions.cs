using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenTK.Graphics.OpenGL4;
using System.Collections;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class ShaderFunctions
    {
        [GMLFunction("shader_set")]
        public static object? shader_set(object?[] args)
        {
            var shaderId = args[0].Conv<int>();
            ShaderManager.ShaderSet(shaderId);
            return null;
        }

        // shader_get_name

        [GMLFunction("shader_reset")]
        public static object? shader_reset(object?[] args)
        {
            ShaderManager.ShaderReset();
            return null;
        }

        [GMLFunction("shader_current")]
        public static object? shader_current(object?[] args)
        {
            return ShaderManager.CurrentShaderIndex;
        }

        // Games commonly fetch a uniform's location from one shader and reuse the raw handle while
        // a DIFFERENT (but similarly-laid-out) shader is bound -- fine on desktop GL, where a
        // location is just a plain int, but WebGL's getUniformLocation return value is bound to the
        // program it came from and throws INVALID_OPERATION if used while another program is
        // active. So instead of handing back that raw, program-bound location, we hand back a
        // synthetic handle carrying just the uniform NAME, and re-resolve it against whichever
        // program is actually active at each shader_set_uniform_* call (see ResolveUniformLocation).
        private static readonly Dictionary<int, string> _uniformHandleNames = new();
        private static int _nextUniformHandle = -2;

        [GMLFunction("shader_get_uniform")]
        public static object? shader_get_uniform(object?[] args)
        {
            var shader = args[0].Conv<int>();
            var uniform = args[1].Conv<string>();

            var runtimeShader = ShaderManager.Shaders[shader];

            if (!runtimeShader.Uniforms.TryGetValue(uniform, out var shaderUniform))
            {
                return -1;
            }

            var handle = _nextUniformHandle--;
            _uniformHandleNames[handle] = uniform;
            return handle;
        }

        private static int ResolveUniformLocation(int handle)
        {
            if (!_uniformHandleNames.TryGetValue(handle, out var name))
            {
                // not one of our synthetic handles (e.g. came from somewhere that still hands back
                // a raw location) -- use it as-is against whatever's currently bound
                return handle;
            }

            return GL.GetUniformLocation(ShaderManager.CurrentProgramID, name);
        }

        [GMLFunction("shader_get_sampler_index")]
        public static object? shader_get_sampler_index(object?[] args)
        {
            var shader = args[0].Conv<int>();
            var sampler = args[1].Conv<string>();

            var runtimeShader = ShaderManager.Shaders[shader];

            var index = runtimeShader.TextureStages.IndexOf(sampler);
            return CustomMath.Max(index, 0);
        }

        [GMLFunction("shader_set_uniform_i")]
        public static object? shader_set_uniform_i(object?[] args)
        {
            var handle = ResolveUniformLocation(args[0].Conv<int>());

            if (handle == -1)
            {
                return null;
            }

            var valueCount = args.Length - 1;
            switch (valueCount)
            {
                case 1:
                    GL.Uniform1(handle, args[1].Conv<int>());
                    break;
                case 2:
                    GL.Uniform2(handle, args[1].Conv<int>(), args[2].Conv<int>());
                    break;
                case 3:
                    GL.Uniform3(handle, args[1].Conv<int>(), args[2].Conv<int>(), args[3].Conv<int>());
                    break;
                case 4:
                    GL.Uniform4(handle, args[1].Conv<int>(), args[2].Conv<int>(), args[3].Conv<int>(), args[4].Conv<int>());
                    break;
                default:
                    throw new NotImplementedException();
            }

            return null;
        }

        [GMLFunction("shader_set_uniform_i_array")]
        public static object? shader_set_uniform_i_array(object?[] args)
        {
            var handle = ResolveUniformLocation(args[0].Conv<int>());
            var arr = args[1].Conv<IList>().ConvAll<int>().ToArray();

            if (handle == -1)
            {
                return null;
            }

            GL.Uniform1(handle, arr.Length, arr);

            return null;
        }

        [GMLFunction("shader_set_uniform_f")]
        public static object? shader_set_uniform_f(object?[] args)
        {
            var handle = ResolveUniformLocation(args[0].Conv<int>());

            if (handle == -1)
            {
                return null;
            }

            var valueCount = args.Length - 1;
            switch (valueCount)
            {
                case 1:
                    GL.Uniform1(handle, args[1].Conv<float>());
                    break;
                case 2:
                    GL.Uniform2(handle, args[1].Conv<float>(), args[2].Conv<float>());
                    break;
                case 3:
                    GL.Uniform3(handle, args[1].Conv<float>(), args[2].Conv<float>(), args[3].Conv<float>());
                    break;
                case 4:
                    GL.Uniform4(handle, args[1].Conv<float>(), args[2].Conv<float>(), args[3].Conv<float>(), args[4].Conv<float>());
                    break;
                default:
                    throw new NotImplementedException();
            }

            return null;
        }

        [GMLFunction("shader_set_uniform_f_array")]
        public static object? shader_set_uniform_f_array(object?[] args)
        {
            var handle = ResolveUniformLocation(args[0].Conv<int>());
            var arr = args[1].Conv<IList>().ConvAll<float>().ToArray();

            if (handle == -1)
            {
                return null;
            }

            GL.Uniform1(handle, arr.Length, arr);

            return null;
        }

        // shader_set_uniform_matrix
        // shader_set_uniform_matrix_array

        [GMLFunction("shader_is_compiled")]
        public static object? shader_is_compiled(object?[] args)
        {
            var shaderId = args[0].Conv<int>();

            // ShaderManager.CompileShaders() compiles every shader resource up front and throws on
            // the first failure, which aborts startup entirely rather than leaving a partially-
            // compiled Shaders table -- so by the time any GML runs, every id in Shaders did compile.
            return ShaderManager.Shaders.ContainsKey(shaderId);
        }

        [GMLFunction("shaders_are_supported")]
        public static object? shaders_are_supported(object?[] args)
        {
            // We only run at all on top of WebGL, which guarantees GLSL ES shader support, so this
            // is unconditionally true rather than a real capability check.
            return true;
        }

        [GMLFunction("texture_set_stage")]
        public static object? texture_set_stage(object?[] args)
        {
            var stage = args[0].Conv<int>();

            if (args[1] is SurfaceTexture surfaceTex)
            {
                GL.BindTextureUnit(stage, surfaceTex.GLHandle);
                return null;
            }

            var tex = args[1] as SpritePageItem;

            if (tex == null)
            {
                throw new NotImplementedException();
            }

            var (image, id) = PageManager.TexturePages[tex.Page];

            GL.BindTextureUnit(stage, id);
            return null;
        }

        [GMLFunction("texture_get_texel_width")]
        public static object? texture_get_texel_width(object?[] args)
        {
            // HACK: surface_get_texture returns -1
            // intercept it and return a dummy value
            if (args[0] is int i && i == -1)
            {
                DebugLog.LogVerbose("Returning dummy value for texture_get_texel_width");
                return 1.0 / CustomWindow.Instance.ClientSize.X;
            }

            var tex = args[0] as SpritePageItem;

            if (tex == null)
            {
                throw new NotImplementedException();
            }

            var (image, id) = PageManager.TexturePages[tex.Page];

            return 1.0 / image.Width;
        }

        [GMLFunction("texture_get_texel_height")]
        public static object? texture_get_texel_height(object?[] args)
        {
            // HACK: surface_get_texture returns -1
            // intercept it and return a dummy value
            if (args[0] is int i && i == -1)
            {
                DebugLog.LogVerbose("Returning dummy value for texture_get_texel_height");
                return 1.0 / CustomWindow.Instance.ClientSize.Y;
            }

            var tex = args[0] as SpritePageItem;

            if (tex == null)
            {
                throw new NotImplementedException();
            }

            var (image, id) = PageManager.TexturePages[tex.Page];

            return 1.0 / image.Height;
        }
    }
}
