using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace OpenGM.Rendering;

public static class ShaderManager
{
    public static int DefaultProgram;

    public static Dictionary<int, RuntimeShader> Shaders = new();

    public static void CompileShaders()
    {
        DefaultProgram = CompileShader("default shader", File.ReadAllText("shader.vsh"), File.ReadAllText("shader.fsh"));
        CurrentProgramID = DefaultProgram;

        Shaders.Clear();

        foreach (var (shaderIndex, shader) in GameLoader.Shaders)
        {
            var program = CompileGameShader(shader);

            var runtimeShader = new RuntimeShader();
            runtimeShader.Name = shader.Name;
            runtimeShader.ShaderIndex = shaderIndex;
            runtimeShader.ProgramID = program;
            FindUniforms(runtimeShader);

            Shaders.Add(shaderIndex, runtimeShader);
        }

        ShaderReset(); // use default
    }

    /// <summary>
    /// Compiles one of the game's own shader resources, which -- unlike the engine's default
    /// shader -- must never be able to take the whole game down with it.
    ///
    /// A shader that doesn't compile used to throw straight out of <see cref="CompileShaders"/>
    /// and abort the load, so a game was unplayable over one effect its renderer could have simply
    /// gone without. That is the wrong trade for an emulator: real GameMaker compiled these at
    /// build time on a platform this one isn't, and the browser build in particular feeds WebGL2
    /// GameMaker's GLSL ES output, which the game itself very likely never compiled even once (it
    /// shipped against the desktop GLSL variant). Two fallbacks, in order of how much of the
    /// game's intent they keep: retry as GLSL ES 3.00 (see <see cref="GlslEs300Upgrade"/>), then
    /// draw with the default shader and say so.
    /// </summary>
    private static int CompileGameShader(SerializedFiles.Shader shader)
    {
        try
        {
            return CompileShader(shader.Name, shader.VertexSource, shader.FragmentSource);
        }
        catch (Exception asShipped)
        {
            if (GlslEs300Upgrade.TryUpgrade(shader.VertexSource, fragment: false, out var vertex)
                && GlslEs300Upgrade.TryUpgrade(shader.FragmentSource, fragment: true, out var fragment))
            {
                try
                {
                    var program = CompileShader(shader.Name, vertex, fragment);
                    DebugLog.LogWarning(
                        $"Shader {shader.Name} only compiles as GLSL ES 3.00; using the upgraded source. As shipped: {asShipped.Message}");
                    return program;
                }
                catch (Exception asEs300)
                {
                    DebugLog.LogWarning($"Shader {shader.Name} failed to compile as GLSL ES 3.00 too: {asEs300.Message}");
                }
            }

            // The game still runs; anything drawn through this shader just misses its effect.
            DebugLog.LogError(
                $"Shader {shader.Name} does not compile, drawing it with the default shader instead.\n\n{asShipped.Message}");
            return DefaultProgram;
        }
    }

    public static void FindUniforms(RuntimeShader shader)
    {
        var stage = 0;
        void PushTextureStage(string name)
        {
            shader.TextureStages.Insert(stage, name);

            var location = GL.GetUniformLocation(shader.ProgramID, name);
            GL.Uniform1(location, stage);
            stage++;
        }

        GL.UseProgram(shader.ProgramID);

        PushTextureStage("gm_BaseTexture");

        GL.GetProgram(shader.ProgramID, GetProgramParameterName.ActiveUniforms, out var count);

        GL.GetProgram(shader.ProgramID, GetProgramParameterName.ActiveUniformMaxLength, out var maxLength);

        for (var i = 0; i < count; i++)
        {
            GL.GetActiveUniform(shader.ProgramID, i, maxLength, out _, out var size, out var type, out var name);

            if (!shader.TextureStages.Contains(name) && type is ActiveUniformType.Sampler2D or ActiveUniformType.SamplerCube)
            {
                PushTextureStage(name);
            }

            if (name.EndsWith("[0]"))
            {
                name = name[..^3];
            }

            shader.Uniforms.Add(name, new()
            {
                Location = GL.GetUniformLocation(shader.ProgramID, name),
                Name = name,
                Size = size,
                Type = type
            });
        }
    }

    private static int CompileShader(string name, string vertSource, string fragSource)
    {
        var vertexShader = CompileShaderHalf(name, ShaderType.VertexShader, vertSource);
        var fragmentShader = CompileShaderHalf(name, ShaderType.FragmentShader, fragSource);

        var program = GL.CreateProgram();
        GraphicsManager.LabelObject(ObjectLabelIdentifier.Program, program, name);
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);

        // Our vertex buffer always feeds position/color/uv at fixed locations 0/1/2 (see
        // shader.vsh's explicit layout(location=N) qualifiers), but user shaders compiled from a
        // game's own shader resources typically use plain "attribute" declarations with no
        // explicit layout -- without forcing these locations before linking, the driver is free to
        // auto-assign them to whatever it likes, which can silently scramble which vertex stream
        // (position vs color vs uv) each attribute actually reads despite the program compiling and
        // linking without error. bindAttribLocation is a no-op for names a given shader doesn't
        // declare, so binding every known GM attribute-name variant unconditionally is safe.
        GL.BindAttribLocation(program, 0, "in_Position");
        GL.BindAttribLocation(program, 1, "in_Colour");
        GL.BindAttribLocation(program, 2, "in_TextureCoord");
        GL.BindAttribLocation(program, 0, "a_pos");
        GL.BindAttribLocation(program, 1, "a_color");
        GL.BindAttribLocation(program, 2, "a_uv");

        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var code);
        if (code != (int)All.True)
        {
            var infoLog = GL.GetProgramInfoLog(program);
            throw new Exception($"Error while linking program {name}.\n\n{infoLog}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }

    private static int CompileShaderHalf(string name, ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GraphicsManager.LabelObject(ObjectLabelIdentifier.Shader, shader, $"{name} {type}");
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var code);
        if (code != (int)All.True)
        {
            var infoLog = GL.GetShaderInfoLog(shader);
            throw new Exception($"Error while compiling shader {name} {type}\n\n{infoLog}");
        }

        return shader;
    }

    public static int gm_Matrices;
    public static int gm_AlphaTestEnabled;
    public static int gm_AlphaRefValue;
    public static int gm_FogStart;
    public static int gm_RcpFogRange;
    public static int gm_PS_FogEnabled;
    public static int gm_FogColour;
    public static int gm_VS_FogEnabled;

    public static int CurrentShaderIndex;
    public static int CurrentProgramID;

    public static void ShaderSet(int index)
    {
        GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD program
        CurrentShaderIndex = index;
        var shader = Shaders[index];
        CurrentProgramID = shader.ProgramID;
        GL.UseProgram(shader.ProgramID);
        AttachUniforms(shader.ProgramID);
        GraphicsManager.SetViewArea(GraphicsManager.ViewArea, GraphicsManager.ViewAreaAngle); // hack to keep gm_matrices values when changing shader
    }

    public static void ShaderReset()
    {
        GraphicsManager.FlushPolygonBatch(); // queued geometry belongs to the OLD program
        CurrentShaderIndex = -1;
        CurrentProgramID = DefaultProgram;
        GL.UseProgram(DefaultProgram);
        AttachUniforms(DefaultProgram);
        GraphicsManager.SetViewArea(GraphicsManager.ViewArea, GraphicsManager.ViewAreaAngle); // hack to keep gm_matrices values when changing shader
    }

    private static void AttachUniforms(int program)
    {
        gm_Matrices = GL.GetUniformLocation(program, "gm_Matrices");
        gm_AlphaTestEnabled = GL.GetUniformLocation(program, "gm_AlphaTestEnabled");
        gm_AlphaRefValue = GL.GetUniformLocation(program, "gm_AlphaRefValue");
        gm_FogStart = GL.GetUniformLocation(program, "gm_FogStart");
        gm_RcpFogRange = GL.GetUniformLocation(program, "gm_RcpFogRange");
        gm_PS_FogEnabled = GL.GetUniformLocation(program, "gm_PS_FogEnabled");
        gm_FogColour = GL.GetUniformLocation(program, "gm_FogColour");
        gm_VS_FogEnabled = GL.GetUniformLocation(program, "gm_VS_FogEnabled");
    }

    // LoadMatrices used to live here, reading the real world matrix that GraphicsManager.SetViewArea
    // hardcoded to identity -- but nothing ever called it (its only caller, Camera.ApplyMatrices,
    // had no callers either), so it just made the world matrix look wired up while every draw
    // ignored it. GraphicsManager.UploadMatrices is the live path now.
}
