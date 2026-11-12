using System.Text.RegularExpressions;

namespace OpenGM.Rendering;

/// <summary>
/// Rewrites a GameMaker GLSL ES 1.00 shader as GLSL ES 3.00.
///
/// GameMaker emits a GLSL ES variant of every shader alongside the desktop one, and the browser
/// build compiles that variant (see GameConverter.ExportShaders) because WebGL2 rejects desktop
/// GLSL outright. Those sources carry no <c>#version</c> directive, so they compile as GLSL ES
/// 1.00 -- whose rules are much stricter than the desktop GLSL the game was actually written and
/// tested against. The one that bites hardest is array indexing: ES 1.00 only permits a constant
/// or a for-loop index, so a shader that walks an array with anything else ("'i' : Index
/// expression can only contain const or loop symbols") fails to compile even though it is
/// perfectly valid everywhere the game itself ran. ES 3.00, which WebGL2 also accepts, allows it.
///
/// This is deliberately only ever used as a *second attempt*, after the shader as shipped has
/// already failed to compile (see ShaderManager.CompileGameShader). A source-level rewrite of a
/// language this tool does not parse cannot be trusted to leave a working shader working, so it
/// never gets the chance to: the worst a bad rewrite can do is fail to compile too, and fall
/// through to the same default-shader fallback that a shader with no upgrade path gets.
/// </summary>
public static class GlslEs300Upgrade
{
    /// <summary>Name for the fragment output replacing ES 1.00's built-in gl_FragColor.</summary>
    private const string FragColorName = "gm_FragColor";

    /// <summary>
    /// The ES 3.00 form of <paramref name="source"/>, or false when it isn't a shader this can
    /// convert (already versioned as something other than ES 1.00, or empty).
    /// </summary>
    public static bool TryUpgrade(string source, bool fragment, out string upgraded)
    {
        upgraded = "";

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        // A source that already states its version is one GameMaker didn't write the way this
        // expects -- only the implicit ES 1.00 (and an explicit "#version 100") are in scope.
        var version = Regex.Match(source, @"^[ \t]*#[ \t]*version[ \t]+(\d+)[^\r\n]*\r?\n?", RegexOptions.Multiline);
        if (version.Success)
        {
            if (version.Groups[1].Value != "100")
            {
                return false;
            }
            source = source.Remove(version.Index, version.Length);
        }

        // \b on each: a shader is free to have an identifier like `varyingScale` or a struct
        // member named `attributes`, and rewriting the middle of one would break a shader that
        // only needed the version bump.
        source = Regex.Replace(source, @"\bvarying\b", fragment ? "in" : "out");
        if (!fragment)
        {
            source = Regex.Replace(source, @"\battribute\b", "in");
        }

        // The sampling built-ins lost their type suffixes in ES 3.00; the overload is picked from
        // the sampler's own type now. textureCube collapses into texture() as well.
        source = Regex.Replace(source, @"\btexture2D\b", "texture");
        source = Regex.Replace(source, @"\btexture2DProj\b", "textureProj");
        source = Regex.Replace(source, @"\btexture2DLod\b", "textureLod");
        source = Regex.Replace(source, @"\btexture2DProjLod\b", "textureProjLod");
        source = Regex.Replace(source, @"\btextureCube\b", "texture");
        source = Regex.Replace(source, @"\btextureCubeLod\b", "textureLod");

        if (fragment)
        {
            // gl_FragColor and gl_FragData are gone in ES 3.00 -- a fragment shader declares its
            // own output instead.
            var writesFragColor = Regex.IsMatch(source, @"\bgl_FragColor\b");
            var writesFragData = Regex.IsMatch(source, @"\bgl_FragData\s*\[\s*0\s*\]");
            source = Regex.Replace(source, @"\bgl_FragColor\b", FragColorName);
            source = Regex.Replace(source, @"\bgl_FragData\s*\[\s*0\s*\]", FragColorName);

            var header = "";
            // vec4 needs a declared default float precision in a fragment shader, and the output
            // declaration below is the first thing that would need it. GameMaker's own ES output
            // states one, so this is only for a shader that somehow doesn't.
            if (!Regex.IsMatch(source, @"\bprecision\b[^;]*\bfloat\b[^;]*;"))
            {
                header += "precision highp float;\n";
            }
            if (writesFragColor || writesFragData)
            {
                header += $"out vec4 {FragColorName};\n";
            }

            source = InsertAfterPrecisionStatements(source, header);
        }

        upgraded = "#version 300 es\n" + source;
        return true;
    }

    /// <summary>
    /// Splices <paramref name="text"/> in after the shader's trailing precision statement, rather
    /// than at the very top -- the declaration it carries is a vec4, which is only legal once a
    /// default float precision is in scope.
    /// </summary>
    private static string InsertAfterPrecisionStatements(string source, string text)
    {
        if (text.Length == 0)
        {
            return source;
        }

        var at = 0;
        foreach (Match precision in Regex.Matches(source, @"\bprecision\b[^;]*;"))
        {
            at = precision.Index + precision.Length;
        }

        // Past the end of that line, so the insert can't land inside a trailing comment.
        if (at > 0)
        {
            var newline = source.IndexOf('\n', at);
            at = newline < 0 ? source.Length : newline + 1;
        }

        return source.Insert(at, text);
    }
}
