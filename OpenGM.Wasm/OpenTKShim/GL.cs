using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace OpenTK.Graphics.OpenGL4;

/// <summary>
/// Namespace-shadowed replacement for the real OpenTK.Graphics.OpenGL4.GL static class (a P/Invoke
/// binding to a native GL driver with no wasm equivalent). Because OpenGM's rendering code
/// references "GL.Foo(...)" purely by name, and this project doesn't reference the real
/// OpenTK.Graphics package, every existing call site binds to this shim automatically -- zero
/// edits needed to OpenGM's own rendering files. Backed by JSImport calls into webgl2.js, which
/// wraps a real WebGL2RenderingContext. Where WebGL2 has no direct-state-access equivalent for a
/// real GL4 DSA call (CreateTextures, NamedFramebufferTexture, BlitNamedFramebuffer, etc.), the
/// emulation (bind-then-operate, or a small id->size/attachment tracking map) lives in webgl2.js,
/// not here.
/// </summary>
public static partial class GL
{
    private const string Module = "webgl2";

    // ---- textures ----

    [JSImport("createTexture", Module)]
    private static partial int CreateTexture1();

    public static void CreateTextures(TextureTarget target, int n, out int texture)
    {
        texture = CreateTexture1();
    }

    [JSImport("textureStorage2D", Module)]
    private static partial void TextureStorage2DInternal(int texture, int levels, int width, int height);

    public static void TextureStorage2D(int texture, int levels, SizedInternalFormat internalFormat, int width, int height)
        => TextureStorage2DInternal(texture, levels, width, height);

    [JSImport("textureSubImage2DBytes", Module)]
    private static partial void TextureSubImage2DBytes(int texture, int level, int xoffset, int yoffset, int width, int height, byte[] pixels);

    public static void TextureSubImage2D(int texture, int level, int xoffset, int yoffset, int width, int height, PixelFormat format, PixelType type, byte[] pixels)
        => TextureSubImage2DBytes(texture, level, xoffset, yoffset, width, height, pixels);

    [JSImport("textureParameter", Module)]
    private static partial void TextureParameterInternal(int texture, string pname, int param);

    public static void TextureParameter(int texture, TextureParameterName pname, int param)
        => TextureParameterInternal(texture, pname.ToString(), param);

    [JSImport("bindTextureUnit", Module)]
    public static partial void BindTextureUnit(int unit, int texture);

    [JSImport("bindTexture", Module)]
    private static partial void BindTextureInternal(int texture);

    public static void BindTexture(TextureTarget target, int texture) => BindTextureInternal(texture);

    [JSImport("deleteTexture", Module)]
    public static partial void DeleteTexture(int texture);

    [JSImport("getTextureWidth", Module)]
    private static partial int GetTextureWidthInternal(int texture);

    [JSImport("getTextureHeight", Module)]
    private static partial int GetTextureHeightInternal(int texture);

    public static void GetTextureLevelParameter(int texture, int level, GetTextureParameter pname, out int value)
        => value = pname == GetTextureParameter.TextureWidth ? GetTextureWidthInternal(texture) : GetTextureHeightInternal(texture);

    [JSImport("getTextureSubImage", Module)]
    private static partial byte[] GetTextureSubImageInternal(int texture, int x, int y, int width, int height);

    public static void GetTextureSubImage(int texture, int level, int xoffset, int yoffset, int zoffset, int width, int height, int depth, PixelFormat format, PixelType type, int bufSize, byte[] pixels)
    {
        var result = GetTextureSubImageInternal(texture, xoffset, yoffset, width, height);
        Array.Copy(result, pixels, Math.Min(result.Length, pixels.Length));
    }

    // ---- buffers / vertex arrays ----

    [JSImport("genVertexArray", Module)]
    public static partial int GenVertexArray();

    [JSImport("genBuffer", Module)]
    public static partial int GenBuffer();

    [JSImport("bindVertexArray", Module)]
    public static partial void BindVertexArray(int vao);

    [JSImport("bindBuffer", Module)]
    private static partial void BindBufferInternal(int buffer);

    public static void BindBuffer(BufferTarget target, int buffer) => BindBufferInternal(buffer);

    [JSImport("vertexAttribPointer", Module)]
    private static partial void VertexAttribPointerInternal(int index, int size, bool normalized, int stride, int offset);

    public static void VertexAttribPointer(int index, int size, VertexAttribPointerType type, bool normalized, int stride, int offset)
        => VertexAttribPointerInternal(index, size, normalized, stride, offset);

    [JSImport("enableVertexAttribArray", Module)]
    public static partial void EnableVertexAttribArray(int index);

    [JSImport("bufferDataBytes", Module)]
    private static partial void BufferDataBytes(byte[] data);

    public static unsafe void BufferData<T>(BufferTarget target, int size, ref T data, BufferUsageHint usage) where T : unmanaged
    {
        fixed (T* ptr = &data)
        {
            var bytes = new ReadOnlySpan<byte>(ptr, size).ToArray();
            BufferDataBytes(bytes);
        }
    }

    // PrimitiveType's values are declared to match the real WebGL/GL enum constants (0x0000-0x0006
    // for the members actually used) -- passing the raw int avoids a ToString() allocation plus a
    // JS-side string->enum dictionary lookup on every single draw call.
    [JSImport("drawArrays", Module)]
    private static partial void DrawArraysInternal(int mode, int first, int count);

    public static void DrawArrays(PrimitiveType mode, int first, int count) => DrawArraysInternal((int)mode, first, count);

    // ---- viewport / clear ----

    [JSImport("viewport", Module)]
    public static partial void Viewport(int x, int y, int w, int h);

    [JSImport("clearColor", Module)]
    private static partial void ClearColorInternal(float r, float g, float b, float a);

    public static void ClearColor(float r, float g, float b, float a) => ClearColorInternal(r, g, b, a);
    public static void ClearColor(Color4 color) => ClearColorInternal(color.R, color.G, color.B, color.A);

    [JSImport("clear", Module)]
    private static partial void ClearInternal(int mask);

    public static void Clear(ClearBufferMask mask) => ClearInternal((int)mask);

    // ---- state ----

    [JSImport("enable", Module)]
    private static partial void EnableInternal(string cap);
    public static void Enable(EnableCap cap)
    {
        if (cap == EnableCap.Blend)
            _blendEnabled = true;
        EnableInternal(cap.ToString());
    }

    // ---- depth / culling ----
    // Added for the d3d_* family and gpu_set_ztestenable/_zwriteenable/_zfunc/_cullmode. The enum
    // values are the real GL constants (see GLEnums.cs), so these pass straight through.

    [JSImport("depthMask", Module)]
    private static partial void DepthMaskInternal(bool flag);
    public static void DepthMask(bool flag) => DepthMaskInternal(flag);

    [JSImport("depthFunc", Module)]
    private static partial void DepthFuncInternal(int func);
    public static void DepthFunc(DepthFunction func) => DepthFuncInternal((int)func);

    [JSImport("cullFace", Module)]
    private static partial void CullFaceInternal(int mode);
    public static void CullFace(TriangleFace mode) => CullFaceInternal((int)mode);

    [JSImport("frontFace", Module)]
    private static partial void FrontFaceInternal(int mode);
    public static void FrontFace(FrontFaceDirection mode) => FrontFaceInternal((int)mode);

    [JSImport("disable", Module)]
    private static partial void DisableInternal(string cap);
    public static void Disable(EnableCap cap)
    {
        if (cap == EnableCap.Blend)
            _blendEnabled = false;
        DisableInternal(cap.ToString());
    }

    [JSImport("blendFunc", Module)]
    private static partial void BlendFuncInternal(string src, string dst);
    public static void BlendFunc(BlendingFactor src, BlendingFactor dst) => BlendFuncInternal(src.ToString(), dst.ToString());

    [JSImport("blendFuncSeparate", Module)]
    private static partial void BlendFuncSeparateInternal(string srcRgb, string dstRgb, string srcAlpha, string dstAlpha);
    public static void BlendFuncSeparate(BlendingFactorSrc srcRgb, BlendingFactorDest dstRgb, BlendingFactorSrc srcAlpha, BlendingFactorDest dstAlpha)
        => BlendFuncSeparateInternal(srcRgb.ToString(), dstRgb.ToString(), srcAlpha.ToString(), dstAlpha.ToString());

    [JSImport("blendEquation", Module)]
    private static partial void BlendEquationInternal(string mode);
    public static void BlendEquation(BlendEquationMode mode) => BlendEquationInternal(mode.ToString());

    [JSImport("colorMask", Module)]
    private static partial void ColorMaskInternal(bool r, bool g, bool b, bool a);

    private static bool _lastColorMaskR = true, _lastColorMaskG = true, _lastColorMaskB = true, _lastColorMaskA = true;
    private static bool _blendEnabled = true;

    public static void ColorMask(bool r, bool g, bool b, bool a)
    {
        _lastColorMaskR = r;
        _lastColorMaskG = g;
        _lastColorMaskB = b;
        _lastColorMaskA = a;
        ColorMaskInternal(r, g, b, a);
    }

    public static bool GetBoolean(GetPName pname) => pname == GetPName.Blend && _blendEnabled;

    public static void GetBoolean(GetPName pname, bool[] data)
    {
        if (pname == GetPName.ColorWritemask && data.Length >= 4)
        {
            data[0] = _lastColorMaskR;
            data[1] = _lastColorMaskG;
            data[2] = _lastColorMaskB;
            data[3] = _lastColorMaskA;
        }
    }

    [JSImport("getFramebufferBinding", Module)]
    private static partial int GetFramebufferBindingInternal();

    public static int GetInteger(GetPName pname) => pname == GetPName.FramebufferBinding ? GetFramebufferBindingInternal() : 0;

    // ---- shaders / programs ----

    [JSImport("createShader", Module)]
    private static partial int CreateShaderInternal(string type);
    public static int CreateShader(ShaderType type) => CreateShaderInternal(type.ToString());

    [JSImport("shaderSource", Module)]
    public static partial void ShaderSource(int shader, string source);

    [JSImport("compileShader", Module)]
    public static partial void CompileShader(int shader);

    [JSImport("getShaderParam", Module)]
    private static partial int GetShaderParamInternal(int shader, string pname);

    public static void GetShader(int shader, ShaderParameter pname, out int value) => value = GetShaderParamInternal(shader, pname.ToString());

    [JSImport("getShaderInfoLog", Module)]
    public static partial string GetShaderInfoLog(int shader);

    [JSImport("createProgram", Module)]
    public static partial int CreateProgram();

    [JSImport("attachShader", Module)]
    public static partial void AttachShader(int program, int shader);

    [JSImport("linkProgram", Module)]
    public static partial void LinkProgram(int program);

    [JSImport("getProgramParam", Module)]
    private static partial int GetProgramParamInternal(int program, string pname);

    public static void GetProgram(int program, GetProgramParameterName pname, out int value) => value = GetProgramParamInternal(program, pname.ToString());

    [JSImport("getProgramInfoLog", Module)]
    public static partial string GetProgramInfoLog(int program);

    [JSImport("deleteShader", Module)]
    public static partial void DeleteShader(int shader);

    [JSImport("useProgram", Module)]
    public static partial void UseProgram(int program);

    [JSImport("getUniformLocation", Module)]
    public static partial int GetUniformLocation(int program, string name);

    [JSImport("bindAttribLocation", Module)]
    public static partial void BindAttribLocation(int program, int index, string name);

    [JSImport("getActiveUniformName", Module)]
    private static partial string GetActiveUniformNameInternal(int program, int index);

    [JSImport("getActiveUniformSize", Module)]
    private static partial int GetActiveUniformSizeInternal(int program, int index);

    [JSImport("getActiveUniformType", Module)]
    private static partial string GetActiveUniformTypeInternal(int program, int index);

    public static void GetActiveUniform(int program, int index, int bufSize, out int length, out int size, out ActiveUniformType type, out string name)
    {
        name = GetActiveUniformNameInternal(program, index);
        length = name.Length;
        size = GetActiveUniformSizeInternal(program, index);
        var typeStr = GetActiveUniformTypeInternal(program, index);
        type = typeStr switch
        {
            "sampler2D" => ActiveUniformType.Sampler2D,
            "samplerCube" => ActiveUniformType.SamplerCube,
            _ => ActiveUniformType.Other
        };
    }

    [JSImport("uniform1f", Module)]
    public static partial void Uniform1(int location, float value);

    [JSImport("uniform1i", Module)]
    public static partial void Uniform1(int location, int value);

    [JSImport("uniform2f", Module)]
    public static partial void Uniform2(int location, float x, float y);

    [JSImport("uniform2i", Module)]
    public static partial void Uniform2(int location, int x, int y);

    [JSImport("uniform3f", Module)]
    public static partial void Uniform3(int location, float x, float y, float z);

    [JSImport("uniform3i", Module)]
    public static partial void Uniform3(int location, int x, int y, int z);

    [JSImport("uniform4f", Module)]
    public static partial void Uniform4(int location, float x, float y, float z, float w);

    [JSImport("uniform4i", Module)]
    public static partial void Uniform4(int location, int x, int y, int z, int w);

    public static void Uniform4(int location, Color4 color) => Uniform4(location, color.R, color.G, color.B, color.A);

    [JSImport("uniform1fv", Module)]
    private static partial void Uniform1fvInternal(int location, byte[] bytes);
    public static void Uniform1(int location, int count, float[] value)
        => Uniform1fvInternal(location, MemoryMarshal.AsBytes(value.AsSpan(0, count)).ToArray());

    [JSImport("uniform1iv", Module)]
    private static partial void Uniform1ivInternal(int location, byte[] bytes);
    public static void Uniform1(int location, int count, int[] value)
        => Uniform1ivInternal(location, MemoryMarshal.AsBytes(value.AsSpan(0, count)).ToArray());

    [JSImport("uniformMatrix4fvBytes", Module)]
    private static partial void UniformMatrix4fvBytes(int location, int count, bool transpose, byte[] bytes);

    public static unsafe void UniformMatrix4(int location, int count, bool transpose, float* value)
    {
        var bytes = new ReadOnlySpan<byte>(value, count * 16 * sizeof(float)).ToArray();
        UniformMatrix4fvBytes(location, count, transpose, bytes);
    }

    // ---- framebuffers ----

    [JSImport("bindFramebuffer", Module)]
    private static partial void BindFramebufferInternal(int framebuffer);
    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => BindFramebufferInternal(framebuffer);

    [JSImport("createFramebuffer", Module)]
    private static partial int CreateFramebuffer1();
    public static void CreateFramebuffers(int n, out int framebuffer) => framebuffer = CreateFramebuffer1();

    [JSImport("namedFramebufferTexture", Module)]
    private static partial void NamedFramebufferTextureInternal(int framebuffer, int texture, int level);

    public static void NamedFramebufferTexture(int framebuffer, FramebufferAttachment attachment, int texture, int level)
        => NamedFramebufferTextureInternal(framebuffer, texture, level);

    [JSImport("checkNamedFramebufferStatus", Module)]
    private static partial bool CheckNamedFramebufferStatusInternal(int framebuffer);

    public static FramebufferStatus CheckNamedFramebufferStatus(int framebuffer, FramebufferTarget target)
        => CheckNamedFramebufferStatusInternal(framebuffer) ? FramebufferStatus.FramebufferComplete : FramebufferStatus.FramebufferUndefined;

    [JSImport("deleteFramebuffer", Module)]
    public static partial void DeleteFramebuffer(int framebuffer);

    [JSImport("getFramebufferColorTexture", Module)]
    private static partial int GetFramebufferColorTextureInternal(int framebuffer);

    public static void GetNamedFramebufferAttachmentParameter(int framebuffer, FramebufferAttachment attachment, FramebufferParameterName pname, out int value)
        => value = GetFramebufferColorTextureInternal(framebuffer);

    [JSImport("blitNamedFramebuffer", Module)]
    private static partial void BlitNamedFramebufferInternal(int readFramebuffer, int drawFramebuffer, int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter);

    public static void BlitNamedFramebuffer(int readFramebuffer, int drawFramebuffer, int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, ClearBufferMask mask, BlitFramebufferFilter filter)
        => BlitNamedFramebufferInternal(readFramebuffer, drawFramebuffer, srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, (int)mask, (int)filter);

    [JSImport("readPixels", Module)]
    private static partial byte[] ReadPixelsInternal(int x, int y, int width, int height);

    public static void ReadPixels(int x, int y, int width, int height, PixelFormat format, PixelType type, byte[] pixels)
    {
        var result = ReadPixelsInternal(x, y, width, height);
        Array.Copy(result, pixels, Math.Min(result.Length, pixels.Length));
    }

    // ---- diagnostics / queries: no-ops, not needed to prove rendering ----

    public static void PushDebugGroup(DebugSourceExternal source, int id, int length, string message) { }
    public static void PopDebugGroup() { }
    public static void ObjectLabel(ObjectLabelIdentifier id, int obj, int length, string label) { }
    public static void DebugMessageCallback(DebugProc callback, IntPtr userParam) { }
    public static unsafe void DebugMessageControl(DebugSourceControl source, DebugTypeControl type, DebugSeverityControl severity, int count, int* ids, bool enabled) { }

    [JSImport("getGlString", Module)]
    private static partial string GetGlStringInternal(string name);
    public static string GetString(StringName name) => GetGlStringInternal(name.ToString());

    private static int _queryCounter;
    public static void CreateQueries(QueryTarget target, int n, out int query) => query = ++_queryCounter;
    public static void GetQuery(QueryTarget target, GetQueryParam pname, out int value) => value = 0;
    public static void BeginQuery(QueryTarget target, int query) { }
    public static void EndQuery(QueryTarget target) { }
    public static void GetQueryObject(int query, GetQueryObjectParam pname, out int value) => value = 0;
}
