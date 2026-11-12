// Minimal enum surface matching OpenTK.Graphics.OpenGL4's names for every member OpenGM's own
// rendering code actually references (audited via `grep -rhoE "\bGL\.[A-Za-z0-9]+"` across the
// whole repo). Values are internal to this shim -- nothing marshals them through a real native GL
// ABI, so they only need to be internally consistent with GL.cs/webgl2.js, not numerically match
// real OpenTK/GL constants.
namespace OpenTK.Graphics.OpenGL4;

public enum TextureTarget { Texture2D }
public enum SizedInternalFormat { Rgba8 }
public enum PixelFormat { Rgba }
public enum PixelType { UnsignedByte, Float }
public enum TextureParameterName { TextureMinFilter, TextureMagFilter, TextureWrapS, TextureWrapT }
public enum TextureMinFilter { Nearest, Linear }
public enum TextureMagFilter { Nearest, Linear }

// Wrap modes for gpu_set_tex_repeat. Ordinals only -- webgl2.js maps them to the GL constants.
public enum TextureWrapMode { Repeat, ClampToEdge }

// Depth comparison. Unlike the ordinal-only enums above these carry their REAL GL values, because
// GL.DepthFunc passes the value straight through to gl.depthFunc.
public enum DepthFunction
{
    Never = 0x0200, Less = 0x0201, Equal = 0x0202, Lequal = 0x0203,
    Greater = 0x0204, Notequal = 0x0205, Gequal = 0x0206, Always = 0x0207,
}

// Likewise real GL values, passed straight through to gl.cullFace / gl.frontFace.
public enum TriangleFace { Front = 0x0404, Back = 0x0405, FrontAndBack = 0x0408 }

public enum FrontFaceDirection { Cw = 0x0900, Ccw = 0x0901 }
public enum BufferTarget { ArrayBuffer }
public enum VertexAttribPointerType { Float }
public enum BufferUsageHint { StreamDraw }
public enum PrimitiveType { Points, Lines, LineLoop, LineStrip, Triangles, TriangleStrip, TriangleFan, Polygon, Quads }
public enum FramebufferTarget { Framebuffer, ReadFramebuffer, DrawFramebuffer }
public enum FramebufferAttachment { ColorAttachment0 }
public enum FramebufferStatus { FramebufferComplete, FramebufferUndefined }
public enum FramebufferParameterName { FramebufferAttachmentObjectName }
public enum GetTextureParameter { TextureWidth, TextureHeight }

[Flags]
public enum ClearBufferMask { ColorBufferBit = 1 }

public enum BlitFramebufferFilter { Nearest, Linear }
public enum ShaderType { VertexShader, FragmentShader }
public enum GetProgramParameterName { ActiveUniforms, ActiveUniformMaxLength, LinkStatus }
public enum ActiveUniformType { Sampler2D, SamplerCube, Other }
public enum ShaderParameter { CompileStatus }
public enum All { True = 1, False = 0 }
public enum ObjectLabelIdentifier { Texture, VertexArray, Buffer, Program, Shader, Framebuffer }
public enum EnableCap { DebugOutput, DebugOutputSynchronous, Texture2D, Blend, DepthTest, CullFace }
public enum DebugSourceExternal { DebugSourceApplication }
public enum DebugSourceControl { DontCare }
public enum DebugTypeControl { DontCare, DebugTypePushGroup, DebugTypePopGroup }
public enum DebugSeverityControl { DontCare }
public enum DebugSource { Other }
public enum DebugType { Other, DebugTypeError }
public enum DebugSeverity { Notification }
public enum BlendingFactor
{
    Zero, One, SrcColor, OneMinusSrcColor, SrcAlpha, OneMinusSrcAlpha,
    DstAlpha, OneMinusDstAlpha, DstColor, OneMinusDstColor, SrcAlphaSaturate
}

// Real OpenTK4 keeps these as distinct legacy enum types (used by the src-only/dest-only
// GL.BlendFuncSeparate overload) rather than reusing BlendingFactor for every parameter --
// mirrored here with the same members since 3DFunctions.cs's gpu_set_blendmode_ext_sepalpha
// switches over both by name.
public enum BlendingFactorSrc
{
    Zero, One, SrcColor, OneMinusSrcColor, SrcAlpha, OneMinusSrcAlpha,
    DstAlpha, OneMinusDstAlpha, DstColor, OneMinusDstColor, SrcAlphaSaturate
}

public enum BlendingFactorDest
{
    Zero, One, SrcColor, OneMinusSrcColor, SrcAlpha, OneMinusSrcAlpha,
    DstAlpha, OneMinusDstAlpha, DstColor, OneMinusDstColor, SrcAlphaSaturate
}
public enum BlendEquationMode { FuncAdd, FuncSubtract, FuncReverseSubtract, Min, Max }
public enum GetPName { Blend, ColorWritemask, FramebufferBinding }
public enum StringName { Version, ShadingLanguageVersion }
public enum QueryTarget { TimeElapsed }
public enum GetQueryParam { CurrentQuery }
public enum GetQueryObjectParam { QueryResult }

public delegate void DebugProc(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam);
