using MemoryPack;
using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.VirtualMachine;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenGM;

public static class ExtensionManager
{
    public static List<Extension> Extensions = new();
    public static Dictionary<string, IntPtr> _nativeLibs = new();

    // https://stackoverflow.com/a/26700515
    // this is what lets us make delegates from a Type[] array
    // final element is return type, all elements before are argument types
    //
    // Lazy, not a plain static field: this reaches into System.Linq.Expressions' internal
    // Reflection.Emit-based dynamic delegate type builder, which needs a JIT and is fundamentally
    // unsupported under browser-wasm (throws PlatformNotSupportedException). The only call site
    // (below) is already reached solely from the Windows-only native-DLL-extension path (gated by
    // OperatingSystem.IsWindows() in Init()), so it's never actually needed off Windows -- but a
    // plain static field initializer runs eagerly the moment this class is touched at all
    // (e.g. Init() being called with zero real extensions to iterate), regardless of whether that
    // branch is ever reached. Lazy<T> defers evaluation to the one call site that's already
    // unreachable outside Windows.
    private static readonly Lazy<Func<Type[], Type>> MakeNewCustomDelegate = new(() => (Func<Type[], Type>)Delegate.CreateDelegate(
        typeof(Func<Type[], Type>),
        typeof(Expression)
            .Assembly
            .GetType("System.Linq.Expressions.Compiler.DelegateHelpers")!
            .GetMethod("MakeNewCustomDelegate", BindingFlags.NonPublic | BindingFlags.Static)!
    ));

    public static void Init()
    {
        // Real GM runs each extension file's Init script once, automatically, before the first
        // room's instances are even created -- games rely on it to seed globals their own GML code
        // reads as early as the first room's Create events (see GameConverter.ExportExtensions's own
        // doc comment on InitScript for a concrete example). This has to run regardless of file
        // Kind (including GML, whose functions are just ordinary script entries already loaded
        // elsewhere) and before the per-kind DLL/stub handling below, which `continue`s past GML
        // files entirely.
        foreach (var extension in Extensions)
        {
            foreach (var file in extension.Files)
            {
                if (file.InitScript == -1)
                {
                    continue;
                }

                var code = GameLoader.Codes[file.InitScript];
                var dummy = new GMLObject();
                VMExecutor.ExecuteCode(code, dummy);
            }
        }

        foreach (var extension in Extensions)
        {
            foreach (var file in extension.Files)
            {
                if (file.Kind == ExtensionKind.Generic)
                {
                    StubAllFuncs(file);
                    continue;
                }

                if (file.Kind != ExtensionKind.Dll)
                {
                    continue;
                }

                if (!OperatingSystem.IsWindows())
                {
                    // A handful of well-known extensions have a real (non-native-DLL) reimplementation
                    // instead of just being stubbed out entirely -- see GMFileSystemExtension.cs's own
                    // doc comment for why GMFile.dll specifically needs one (its save/load functions
                    // returning "undefined" instead of real bytes crashes whatever script called them).
                    if (file.Name.Equals("GMFile.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        IO.GMFileSystemExtension.Register(file);
                    }
                    else if (file.Name.Equals("GMIni.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        IO.GMIniExtension.Register(file);
                    }
                    else if (file.Name.Equals("GMResource.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        IO.GMResourceExtension.Register(file);
                    }
                    else
                    {
                        StubAllFuncs(file);
                    }
                    continue;
                }

                if (!_nativeLibs.TryGetValue(file.Name, out var handle))
                {
                    var path = Path.Join(Entry.DataWinFolder, file.Name);
                    var file64bit = Is64BitPe(path) ?? false;
                    var system64bit = Environment.Is64BitProcess;

                    if (file64bit != system64bit)
                    {
                        DebugLog.LogError($"Can't load {file.Name} (DLL is {(file64bit ? "64" : "32")}-bit, process is {(system64bit ? "64" : "32")}-bit)");
                        StubAllFuncs(file);
                        continue;
                    }

                    // cd to the dll's folder so it can find any other dlls it may need
                    var lastDir = Directory.GetCurrentDirectory();
                    Directory.SetCurrentDirectory(Entry.DataWinFolder);

                    // load the library, if it fails then get the win32 error and throw it
                    handle = WinLoadLibrary(path);
                    if (handle == 0)
                    {
                        var err = Marshal.GetLastWin32Error();
                        throw new Exception($"Failed to load extension DLL at {path} (error code {err})");
                    }

                    // cd back to last dir and add library handle to _nativeLibs
                    Directory.SetCurrentDirectory(lastDir);
                    _nativeLibs[file.Name] = handle;
                }

                foreach (var func in file.Functions)
                {
                    var types = new List<Type>();

                    // add types of every argument
                    foreach (var arg in func.Arguments)
                    {
                        types.Add(arg switch
                        {
                            ExtensionVarType.String => typeof(char*),
                            ExtensionVarType.Double => typeof(double),
                            _ => throw new UnreachableException()
                        });
                    }

                    // add return type as last element
                    types.Add(func.ReturnType switch
                    {
                        ExtensionVarType.String => typeof(char*),
                        ExtensionVarType.Double => typeof(double),
                        _ => throw new UnreachableException()
                    });

                    // get the address of the function based on its external name
                    var funcPtr = WinGetProcAddress(handle, func.ExternalName);
                    if (funcPtr == 0)
                    {
                        var err = Marshal.GetLastWin32Error();
                        throw new Exception($"Failed to load extension function {func.ExternalName} (error code {err})");
                    }

                    DebugLog.LogVerbose($"{func.ExternalName}({func.Arguments.Count} args): 0x{funcPtr:X16}");

                    // create delegate type
                    var deleType = MakeNewCustomDelegate.Value([.. types]);

                    // convert the address of our external function to said delegate type
                    var dele = Marshal.GetDelegateForFunctionPointer(funcPtr, deleType);

                    // wrap the delegate in another function and throw it into BuiltInFunctions
                    var success = ScriptResolver.BuiltInFunctions.TryAdd(func.Name, ConvertExtensionFunc(dele, types));
                    if (!success)
                    {
                        DebugLog.LogError($"Could not add extension function {func.Name} to functions list");
                    }
                }
            }
        }
    }

    internal static ScriptResolver.GMLFunctionType MakeStubFunction(string functionName) =>
        (object?[] args) =>
        {
            var alwaysLog = ScriptResolver.AlwaysLogStubs;
            var logged = ScriptResolver.LoggedStubs;

            if (alwaysLog || !logged.Contains(functionName))
            {
                if (!alwaysLog)
                {
                    logged.Add(functionName);
                }

                DebugLog.LogWarning($"Extension function {functionName} stubbed out.");
            }

            return null;
        };

    static ScriptResolver.GMLFunctionType ConvertExtensionFunc(Delegate dele, List<Type> types) =>
        (object?[] args) =>
        {
            var pins = new GCHandle?[args.Length];
            var newArgs = new List<object?>();
            try
            {
                for (var i = 0; i < types.Count - 1; i++)
                {
                    if (args.Length <= i)
                    {
                        if (types[i] == typeof(double))
                        {
                            newArgs.Add((double)0);
                        }
                        else
                        {
                            // TODO: are strings just null pointers?
                            newArgs.Add((nint)0);
                        }

                        continue;
                    }

                    if (args[i] is string str)
                    {
                        var bytes = Encoding.ASCII.GetBytes(str + char.MinValue);

                        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                        pins[i] = pin;
                        newArgs.Add(pin.AddrOfPinnedObject());
                        continue;
                    }

                    newArgs.Add(args[i]);
                }

                var result = dele.DynamicInvoke([.. newArgs]);

                return result;
            }
            finally
            {
                foreach (var pin in pins)
                {
                    pin?.Free();
                }
            }
        };

    static void StubAllFuncs(ExtensionFile file)
    {
        foreach (var func in file.Functions)
        {
            var success = ScriptResolver.BuiltInFunctions.TryAdd(func.Name, MakeStubFunction(func.Name));
            if (!success)
            {
                DebugLog.LogError($"Could not add stub for extension function {func.Name}");
            }
        }
    }

    static bool? Is64BitPe(string path)
    {
        var stream = File.OpenRead(path);

        // find DOS header (MZ)
        var header = new byte[2];
        stream.ReadExactly(header);
        Console.WriteLine(header);
        if (!Enumerable.SequenceEqual(header, "MZ"u8.ToArray()))
        {
            return null;
        }

        // find PE address
        stream.Seek(0x3c, SeekOrigin.Begin);

        var peAddr = new byte[4];
        stream.ReadExactly(peAddr);
        var peAddrInt = BitConverter.ToInt32(peAddr);

        // find PE header (PE\0\0)
        stream.Seek(peAddrInt, SeekOrigin.Begin);

        var peHeader = new byte[4];
        stream.ReadExactly(peHeader);
        if (!Enumerable.SequenceEqual(peHeader, (byte[])[0x50, 0x45, 0x00, 0x00]))
        {
            return null;
        }

        // find arch header (0x8664 for 64-bit)
        var archHeader = new byte[2];
        stream.ReadExactly(archHeader);
        stream.Close();

        return Enumerable.SequenceEqual(archHeader, (byte[])[0x64, 0x86]);
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibrary", SetLastError = true)]
    static extern IntPtr WinLoadLibrary([MarshalAs(UnmanagedType.LPStr)] string lpLibFileName);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
    static extern IntPtr WinGetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

    [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
    static extern bool WinFreeLibrary(IntPtr hModule);
}

[MemoryPackable]
public partial class Extension
{
    public string Name = null!;
    public string Version = null!;
    public List<ExtensionFile> Files = new();
}

[MemoryPackable]
public partial class ExtensionFile
{
    public string Name = null!;
    public int CleanupScript = -1;
    public int InitScript = -1;
    public ExtensionKind Kind;
    public List<ExtensionFunction> Functions = new();
}

[MemoryPackable]
public partial class ExtensionFunction
{
    public uint Id;
    public string Name = null!;
    public string ExternalName = null!;
    public ExtensionVarType ReturnType;
    public List<ExtensionVarType> Arguments = new();
}

public enum ExtensionVarType : uint
{
    String = 1,
    Double = 2
}

public enum ExtensionKind : uint
{
    Unknown0 = 0,
    Dll = 1,
    GML = 2,
    ActionLib = 3,
    Generic = 4,
    Js = 5
}
