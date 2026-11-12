using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.VirtualMachine;

namespace OpenGM.IO;

public static class DebugLog
{
    public static LogType Verbosity = LogType.Info;

    // Console.ForegroundColor/ResetColor throw PlatformNotSupportedException under browser-wasm
    // (there's no real terminal to colorize) -- plain Console.WriteLine is unaffected and still
    // reaches the browser console fine, so only the color-setting calls need guarding.
    private static void SetColor(ConsoleColor color)
    {
        if (!OperatingSystem.IsBrowser())
        {
            Console.ForegroundColor = color;
        }
    }

    private static void ResetColor()
    {
        if (!OperatingSystem.IsBrowser())
        {
            Console.ResetColor();
        }
    }

    public static void Log(string message, LogType? type = null)
    {
        if (Verbosity < (type ?? LogType.Info))
        {
            return;
        }

        Action<string> logFunc = type switch
        {
            LogType.Error => LogError,
            LogType.Warning => LogWarning,
            LogType.Info => LogInfo,
            LogType.Verbose => LogVerbose,
            _ => Console.WriteLine
        };

        logFunc.Invoke(message);
    }

    public static void LogWarning(string message)
    {
        if (Verbosity < LogType.Warning)
        {
            return;
        }

        SetColor(ConsoleColor.Yellow);
        Console.WriteLine(message);
        ResetColor();
    }

    public static void LogInfo(string message)
    {
        if (Verbosity < LogType.Info)
        {
            return;
        }

        SetColor(ConsoleColor.Cyan);
        Console.WriteLine(message);
        ResetColor();
    }

    public static void LogError(string message)
    {
        if (Verbosity < LogType.Error)
        {
            return;
        }

        SetColor(ConsoleColor.Red);
        Console.WriteLine(message);
        ResetColor();
    }

    public static void LogVerbose(string message)
    {
        if (Verbosity < LogType.Verbose)
        {
            return;
        }

        SetColor(ConsoleColor.Magenta);
        Console.WriteLine(message);
        ResetColor();
    }

    /// <summary>
    /// Backs GML's own show_debug_message -- unlike every other Log* method here, this is never
    /// gated by Verbosity. Real GM always prints show_debug_message output to its debug console
    /// regardless of any engine-side verbosity setting; it's explicit, game-authored debug output; a
    /// game/engine dev relying on it (as this project's own investigations have, e.g. instrumenting
    /// YoYoYo_Engine to trace a collision-order bug) shouldn't have it silently eaten by
    /// WasmEntry.cs's browser-build default of only showing Warning+ (there to cut down OpenGM's own
    /// internal Info-level engine chatter, never meant to also swallow the game's own debug calls).
    /// </summary>
    public static void LogUser(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// Frames whose disassembly has already been printed, so a failure inside a Draw event dumps it
    /// once rather than on every frame for as long as the game is open.
    /// </summary>
    private static readonly HashSet<(string Code, int Index)> _dumpedFrames = new();

    /// <param name="withDisassembly">
    /// Also print the handful of instructions leading up to each frame's current position. What a
    /// failing instruction was handed is usually built either by the instructions just before it or
    /// by the *caller* -- an argument passed down from a frame further up -- so a dump of only the
    /// frame that died explains half the cases and leaves the other half looking impossible. Printed
    /// once per position, however many times the failure repeats.
    /// </param>
    public static void PrintCallStack(LogType? type = null, bool withDisassembly = false)
    {
        Log($"--Stacktrace--", type);
        foreach (var item in VMExecutor.CallStack)
        {
            // Not every frame's code is a loaded game asset: a hand-written script (the test suite
            // runs plenty) never reaches GameLoader.Codes, and First() threw looking for it --
            // out of the error path, so it replaced whatever had actually gone wrong with a
            // LINQ exception. Report the frame either way and skip only the part that needs the
            // code.
            var code = GameLoader.Codes.Values.FirstOrDefault(x => x.Name == item.CodeName);

            var lastLabel = 0;
            foreach (var (label, index) in code?.Labels ?? [])
            {
                if (index <= item.InstructionIndex && lastLabel < label)
                {
                    lastLabel = label;
                }
            }

            Log($" - {item.CodeName} | {item.EnvFrame} | @ Ins:{item.InstructionIndex} Label:{lastLabel}", type);

            if (code is null || !withDisassembly || !_dumpedFrames.Add((item.CodeName, item.InstructionIndex)))
            {
                continue;
            }

            const int Context = 10;
            var from = Math.Max(0, item.InstructionIndex - Context);
            for (var i = from; i <= item.InstructionIndex && i < code.Instructions.Count; i++)
            {
                Log($"    {(i == item.InstructionIndex ? "->" : "  ")} {i,5}: {code.Instructions[i].Raw}", type);
            }
        }
    }

    public static void PrintInstances(LogType? type = LogType.Verbose)
    {
        Log("--Instances--", type);
        foreach (var instance in InstanceManager.instances.Values)
        {
            Log($" - {instance.Definition.Name} ({instance.instanceId}) Persistent:{instance.persistent} Active:{instance.Active} Marked:{instance.Marked} Destroyed:{instance.Destroyed}", type);
        }
    }

    public static void PrintInactiveInstances(LogType? type = LogType.Verbose)
    {
        Log("--Inactive Instances--", type);
        foreach (var instance in InstanceManager.inactiveInstances.Values)
        {
            Log($" - {instance.Definition.Name} ({instance.instanceId}) Persistent:{instance.persistent} Active:{instance.Active} Marked:{instance.Marked} Destroyed:{instance.Destroyed}", type);
        }
    }

    public static void PrintDrawObjects(LogType? type = LogType.Verbose)
    {
        Log("--Draw Objects--", type);
        foreach (var item in DrawManager._drawObjects)
        {
            if (item is GamemakerObject gm)
            {
                Log($" - {gm.Definition.Name} ({gm.instanceId}) Depth:{item.depth} Persistent:{gm.persistent} Active:{gm.Active} Marked:{gm.Marked} Destroyed:{gm.Destroyed}", type);
            }
            else
            {
                Log($" - {item.GetType().Name} InstanceID:{item.instanceId} Depth:{item.depth}", type);
            }
        }
    }

    public enum LogType
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Verbose = 3
    }
}
