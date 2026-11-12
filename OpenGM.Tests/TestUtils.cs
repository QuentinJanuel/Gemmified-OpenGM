using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

public static class TestUtils
{
    public static object? ExecuteScript(string name, string asmFile)
    {
        var code = GameConverter.ConvertAssembly(null, asmFile);
        code.Name = name;

        // Any asm that calls a builtin (@@NewGMLObject@@ for `new`, say) needs the [GMLFunction]
        // table populated -- CALL looks there after ScriptsByName and fails the instruction if it
        // misses. Self-guarding, so calling it per script is free.
        ScriptResolver.InitGMLFunctions();
        RegisterDeclaredFunctions(code);

        VMExecutor.VerboseStackLogs = true;
        var result = VMExecutor.ExecuteCode(code, null);

        DebugLog.Log($"result = {result ?? "null value"} ({result?.GetType().ToString() ?? "null type"})");

        return result;
    }

    private static int _nextCodeAssetId = 92_000;

    /// <summary>
    /// An asm body can declare script functions with a <c>&gt; name (locals=.., argc=..)</c> line.
    /// A real game gets those registered as script assets during conversion, but a hand-written asm
    /// string never goes through that -- so <c>push.i [function]name</c> resolved to index -1 and
    /// FetchMethod then indexed the builtin table with it and threw. Register them the way a
    /// converted game would: an empty child VMCode naming the function, pointing at the parent code
    /// that actually holds the instructions (see VMExecutor.ExecuteCode's ParentAssetId handling).
    /// </summary>
    private static void RegisterDeclaredFunctions(VMCode code)
    {
        if (code.Functions.Count == 0)
        {
            return;
        }

        code.AssetId = _nextCodeAssetId++;
        GameLoader.Codes[code.AssetId] = code;

        foreach (var function in code.Functions)
        {
            var index = AssetIndexManager.Register(AssetType.scripts, function.FunctionName);

            var script = new VMScript
            {
                AssetIndex = index,
                Name = function.FunctionName,
                CustomCode = new VMCode
                {
                    Name = function.FunctionName,
                    ParentAssetId = code.AssetId,
                    LocalVariables = new List<string>()
                }
            };

            ScriptResolver.ScriptsByIndex[index] = script;
            ScriptResolver.ScriptsByName[function.FunctionName] = script;
        }
    }
}
