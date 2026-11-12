using OpenGM.IO;
using OpenGM.SerializedFiles;
using System.Collections;

namespace OpenGM.VirtualMachine;

public static partial class VMExecutor
{
    // Real GM never throws reading past an array's current bound -- it just yields undefined
    // (mirrors ArraySet's own graceful auto-grow on the write side, and the "variable doesn't
    // exist" push-undefined handling for missing instance variables elsewhere in this file).
    private static object? SafeArrayIndex(IList array, int index, string varName)
    {
        if (index >= 0 && index < array.Count)
        {
            return array[index];
        }

        // int.MinValue is what casting a NaN double to int lands on, and no real GML index ever
        // reaches it -- so say what actually happened rather than reporting a nonsense number and
        // leaving whoever reads the log to work out where it came from.
        var indexText = index == int.MinValue
            ? "NaN/undefined (something upstream never got a value)"
            : index.ToString();

        DebugLog.LogWarning($"Array index {indexText} out of bounds for {varName} (count {array.Count}), pushing undefined.");
        return null;
    }

    /// <summary>
    /// Reads one <c>[array]</c> index out of whatever a variable happens to be holding.
    ///
    /// Before 2.3, GML has no separate array type: a variable and element 0 of an array of the same
    /// name are one and the same storage, so a game may store a single value and read it back as
    /// <c>v[0]</c>. I Wanna Be The Ocean Princess does that for every input it polls -- its
    /// scrButtonCheck takes a button and immediately indexes it -- and converting a plain number to
    /// an IList threw, which aborted the calling event: every step, for every button, in both the
    /// title menu and the world controller.
    ///
    /// See CompatFlags.ScalarIsFirstArrayElement. From 2.3 arrays are a real type and indexing a
    /// number is a genuine error, so there this still throws rather than inventing an answer.
    /// </summary>
    private static void PushArrayElement(object? container, int index, string varName)
    {
        if (container is IList list)
        {
            Call.Stack.Push(SafeArrayIndex(list, index, varName), VMType.v);
            return;
        }

        if (CompatFlags.ScalarIsFirstArrayElement)
        {
            if (index != 0)
            {
                // Past the end of a one-element array, which reads undefined exactly as
                // SafeArrayIndex would have.
                DebugLog.LogWarning(
                    $"Array index {index} out of bounds for {varName} (a plain value, so count 1), pushing undefined.");
            }

            Call.Stack.Push(index == 0 ? container : null, VMType.v);
            return;
        }

        Call.Stack.Push(SafeArrayIndex(container.Conv<IList>(), index, varName), VMType.v);
    }

    public static void PushGlobal(string varName)
    {
        // Real GM leniently treats a never-written global as 0 rather than erroring (this is what
        // lets code like "if (global.foo) ..." work before anything ever assigns global.foo) --
        // GMLObject's indexer throws KeyNotFoundException on a genuinely missing key, so this needs
        // TryGetValue instead of the indexer to get that same lenient default.
        var value = VariableResolver.GlobalVariables.TryGetValue(varName, out var existing) ? existing : 0;
        Call.Stack.Push(value, VMType.v);
    }

    public static void PushGlobalArrayIndex(string varName, int index)
    {
        if (!VariableResolver.GlobalVariables.TryGetValue(varName, out var existing))
        {
            Call.Stack.Push(0, VMType.v);
            return;
        }

        PushArrayElement(existing, index, varName);
    }

    public static void PushLocalArrayIndex(string varName, int index)
    {
        PushArrayElement(Call.Locals[varName], index, varName);
    }

    public static void PushLocal(string varName)
    {
        Call.Stack.Push(Call.Locals[varName], VMType.v);
    }

    public static void PushBuiltin(string varName)
    {
        if (VariableResolver.BuiltInVariables.ContainsKey(varName))
        {
            var getter = VariableResolver.BuiltInVariables[varName].getter;

            if (getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            var value = getter(0);
            Call.Stack.Push(value, VMType.v);
        }
        // The `is GamemakerObject` guard is load-bearing, and the sibling helpers below already
        // carry it: an INSTANCE builtin only means anything on an instance. A struct's own member
        // can share a name with one -- `x` and `y` are the obvious pair, and a vec2 struct is built
        // out of exactly those -- and the compiler emits those names in builtin scope regardless of
        // what self turns out to be at runtime. Without the guard, `x` inside a struct method ran
        // the GamemakerObject getter against the struct and died on the cast
        // (InvalidCastException), taking the calling event with it. tijit engine 2's entire vector
        // library, its menus and its spike/vine geometry are structs with x/y members.
        else if (VariableResolver.BuiltInSelfVariables.ContainsKey(varName) && Self.Self is GamemakerObject)
        {
            var getter = VariableResolver.BuiltInSelfVariables[varName].getter;

            if (getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            var value = getter(Self.GMSelf);
            Call.Stack.Push(value, VMType.v);
        }
        else if (Self.Self.SelfVariables.ContainsKey(varName))
        {
            var value = Self.Self.SelfVariables[varName];
            Call.Stack.Push(value, VMType.v);
        }
        else if (Call.Function!.StaticVariables.ContainsKey(varName))
        {
            var value = Call.Function!.StaticVariables[varName];
            Call.Stack.Push(value, VMType.v);
        }
        else if (AssetIndexManager.GetIndex(varName) != -1)
        {
            // An asset's own name, which the compiler leaves as a runtime name lookup rather than
            // baking in an index -- room names especially, since `switch (room) case rMenu:` is how
            // a GMS game branches on where it is.
            Call.Stack.Push(AssetIndexManager.GetIndex(varName), VMType.v);
        }
        else
        {
            // A name in builtin scope that resolves to nothing at all. Real GM answers undefined
            // here rather than failing, and games depend on that: tijit engine 2's getMusic() has
            // `switch (room) case r00_01menu:` naming a room that no longer exists in the project,
            // so every room start died on it. Undefined simply never matches the switch, which is
            // exactly what the game does on Windows.
            //
            // Warned about rather than passed over in silence, because the other thing that reaches
            // here is a builtin variable this engine has never heard of, and that IS a gap worth
            // seeing. (A builtin it knows but hasn't implemented still throws above, on the null
            // getter -- that case stays loud.)
            DebugLog.LogWarning($"Reading unknown builtin variable {varName} - answering undefined.");
            Call.Stack.Push(null, VMType.v);
        }
    }

    public static void PushBuiltinArrayIndex(string varName, int index)
    {
        if (VariableResolver.BuiltInVariables.ContainsKey(varName))
        {
            var getter = VariableResolver.BuiltInVariables[varName].getter;

            if (getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            Call.Stack.Push(getter(index), VMType.v);
        }
        else if (VariableResolver.BuiltInSelfVariables.ContainsKey(varName))
        {
            var getter = VariableResolver.BuiltInSelfVariables[varName].getter;

            if (getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            PushArrayElement(getter(Self.GMSelf), index, varName);
        }
        else if (Self.Self.SelfVariables.ContainsKey(varName))
        {
            PushArrayElement(Self.Self.SelfVariables[varName], index, varName);
        }
        else if (Call.Function!.StaticVariables.ContainsKey(varName))
        {
            PushArrayElement(Call.Function!.StaticVariables[varName], index, varName);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    public static void PushSelf(IStackContextSelf self, string varName)
    {
        if (self == null)
        {
            DebugLog.LogError($"Null self given to PushSelf varName:{varName} - {VMExecutor.CurrentInstruction!.Raw}");
            DebugLog.PrintCallStack(DebugLog.LogType.Error);

            Call.Stack.Push(null, VMType.v);
            return;
        }

        if (VariableResolver.BuiltInVariables.TryGetValue(varName, out var builtin_gettersetter))
        {
            if (builtin_gettersetter.getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            Call.Stack.Push(builtin_gettersetter.getter(0), VMType.v);
        }
        else if (VariableResolver.BuiltInSelfVariables.TryGetValue(varName, out var selfbuiltin_gettersetter) && self is GamemakerObject gm)
        {
            if (selfbuiltin_gettersetter.getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            Call.Stack.Push(selfbuiltin_gettersetter.getter(gm), VMType.v);
        }
        else
        {
            if (self.SelfVariables.ContainsKey(varName))
            {
                Call.Stack.Push(self.SelfVariables[varName], VMType.v);
            }
            else
            {
                if (self is GamemakerObject gmo)
                {
                    DebugLog.LogError($"Variable {varName} doesn't exist in {gmo.instanceId} {gmo.Definition.Name}, pushing undefined.");
                }
                else
                {
                    DebugLog.LogError($"Variable {varName} doesn't exist in non-GMO self, pushing undefined.");
                }

                DebugLog.PrintCallStack(DebugLog.LogType.Error);

                self.SelfVariables[varName] = null;
                Call.Stack.Push(self.SelfVariables[varName], VMType.v);
            }
        }
    }

    /// <summary>
    /// The N of <c>argumentN</c>, for the sixteen numbered argument built-ins only -- not bare
    /// <c>argument</c> (a genuinely indexable array), and not <c>argument_count</c> or
    /// <c>argument_relative</c>.
    /// </summary>
    internal static bool TryGetArgumentIndex(string varName, out int argIndex)
    {
        const string prefix = "argument";
        argIndex = -1;
        return varName.Length > prefix.Length
            && varName.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(varName.AsSpan(prefix.Length), out argIndex);
    }

    public static void PushSelfArrayIndex(IStackContextSelf self, string varName, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException($"index into {varName} is negative: {index}");
        }

        // argumentN is not an indexable built-in the way view_xview[i] is. Its N is baked into the
        // name, so VariableResolver registers it as `(_) => get_argument(0)` -- a getter that
        // *discards* the index it is handed. Letting the built-in table answer `argument0[i]` for
        // it therefore returns the whole of argument 0, silently and with no error: a script doing
        // `scrGetKeybind(argument0[0])` on an array argument passed the array itself straight
        // through, and the first builtin downstream that wanted a number died holding it.
        //
        // This lands here rather than on the `argument` branch of the caller because GameMaker
        // tags a script asset's `argument0[i]` as `[array]self.argument0` -- instance type self,
        // not the argument pseudo-instance -- so the name is the only thing to go on.
        if (TryGetArgumentIndex(varName, out _))
        {
            PushArgumentArrayIndex(varName, index);
            return;
        }

        object? container;

        if (VariableResolver.BuiltInVariables.TryGetValue(varName, out var bi_gettersetter))
        {
            if (bi_gettersetter.getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            Call.Stack.Push(bi_gettersetter.getter(index), VMType.v);
            return;
        }

        if (VariableResolver.BuiltInSelfVariables.TryGetValue(varName, out var bis_gettersetter) && self is GamemakerObject gm)
        {
            if (bis_gettersetter.getter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            container = bis_gettersetter.getter(gm);
        }
        else
        {
            if (!self.SelfVariables.ContainsKey(varName))
            {
                if (self is GamemakerObject gmobj)
                {
                    DebugLog.LogError($"{gmobj.instanceId} does not contain the variable {varName}");
                }
                else
                {
                    DebugLog.LogError($"{self} does not contain the variable {varName}");
                }
            }

            container = self.SelfVariables[varName];
        }

        PushArrayElement(container, index, varName);
    }

    public static void PushArgument(int index)
    {
        var arguments = Call.Locals["arguments"].Conv<IList>();

        if (index >= arguments.Count)
        {
            // Scripts can be called with fewer than normal arguments.
            // They just get set to Undefined.
            Call.Stack.Push(null, VMType.v);
            return;
        }

        Call.Stack.Push(arguments[index], VMType.v);
    }

    public static void PushIndex(object? item, string varName)
    {
        var inst = FetchSelf(item);

        if (inst == null)
        {
            // Real GM: reading a variable through an *object asset* reference (e.g.
            // "objPlayer.vspeed") when no live instance of that object currently exists just
            // returns 0 -- no error, no crash. This is a completely normal occurrence (e.g. a
            // collision event queued against an instance that got destroyed earlier the same
            // step, like YoYoYo_Engine's water objects reading objPlayer.vspeed right after a
            // spike killed the player), not a bug, so it shouldn't spam the error log or push
            // undefined (which crashes the next arithmetic op that touches it) the way a
            // genuinely-missing instance id would.
            if (item is int or long or short && (int)item < GMConstants.FIRST_INSTANCE_ID)
            {
                Call.Stack.Push(0, VMType.v);
                return;
            }

            DebugLog.LogError($"Tried to push variable {varName} from {item} ({item?.GetType().Name ?? "null"}), which isn't a valid self!!");
            DebugLog.PrintCallStack(DebugLog.LogType.Error);
            DebugLog.PrintInstances(DebugLog.LogType.Error);

            Call.Stack.Push(null, VMType.v);
            return;
        }

        PushSelf(inst, varName);
    }

    public static void PushOther(string varName)
    {
        PushSelf(Other.Self, varName);
    }

    public static void PushStacktop(string varName)
    {
        var top = Call.Stack.Pop(VMType.v);
        PushIndex(top, varName);
    }

    public static void PushArgumentArrayIndex(string varName, int index)
    {
        if (!TryGetArgumentIndex(varName, out var argIndex))
        {
            throw new ArgumentException($"{varName} is not a numbered argument", nameof(varName));
        }

        var arguments = Call.Locals["arguments"].Conv<IList>();

        if (argIndex >= arguments.Count)
        {
            Call.Stack.Push(null, VMType.v);
            return;
        }

        PushArrayElement(arguments[argIndex], index, varName);
    }

    public static void PushStatic(string varname)
    {
        var currentFunc = Call.Function;

        if (currentFunc == null)
        {
            // uhhhhh fuckin uhhh
            throw new NotImplementedException();
        }

        if (!currentFunc.HasStaticInitRan)
        {
            throw new NotImplementedException($"Tried to push static variable {varname} before static initialization!");
        }

        if (!currentFunc.StaticVariables.TryGetValue(varname, out var value))
        {
            throw new NotImplementedException("StaticVariables should contain every static variable after initialization!?");
        }

        Call.Stack.Push(value, VMType.v);
    }

    public static (ExecutionResult, object?) DoPush(VMCodeInstruction instruction)
    {
        switch (instruction.TypeOne)
        {
            case VMType.i:

                if (instruction.StringData != null)
                {
                    if (instruction.PushFunction)
                    {
                        if (ScriptResolver.BuiltInFunctions.ContainsKey(instruction.StringData))
                        {
                            Call.Stack.Push(ScriptResolver.BuiltInFunctions.Keys.ToList().IndexOf(instruction.StringData), VMType.i);
                        }
                        else
                        {
                            Call.Stack.Push(GMConstants.FIRST_INSTANCE_ID + AssetIndexManager.GetIndex(AssetType.scripts, instruction.StringData), VMType.i);
                        }
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
                else
                {
                    Call.Stack.Push(instruction.IntData, VMType.i);
                }
                return (ExecutionResult.Success, null);
            case VMType.e:
                Call.Stack.Push(instruction.ShortData, VMType.e);
                return (ExecutionResult.Success, null);
            case VMType.l:
                Call.Stack.Push(instruction.LongData, VMType.l);
                return (ExecutionResult.Success, null);
            case VMType.b:
                Call.Stack.Push(instruction.BoolData, VMType.b);
                return (ExecutionResult.Success, null);
            case VMType.d:
                Call.Stack.Push(instruction.DoubleData, VMType.d);
                return (ExecutionResult.Success, null);
            case VMType.s:
                Call.Stack.Push(instruction.StringData, VMType.s);
                return (ExecutionResult.Success, null);
            case VMType.v:
                return DoPushV(instruction);
        }

        return (ExecutionResult.Failed, $"Don't know how to push {instruction.Raw}");
    }

    public static (ExecutionResult, object?) DoPushV(VMCodeInstruction instruction)
    {
        var variableName = instruction.variableName;
        var variableType = instruction.variableType;
        var variablePrefix = instruction.variablePrefix;
        var assetId = instruction.assetId;

        if (variablePrefix == VariablePrefix.None)
        {
            if (variableType == VariableType.Global)
            {
                PushGlobal(variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Local)
            {
                PushLocal(variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.BuiltIn)
            {
                PushBuiltin(variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Self)
            {
                PushSelf(Self.Self, variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Argument)
            {
                var strIndex = variableName[8..]; // skip "argument"
                var index = int.Parse(strIndex);
                PushArgument(index);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Index)
            {
                PushIndex(assetId, variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Other)
            {
                PushOther(variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Stacktop)
            {
                PushStacktop(variableName);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Static)
            {
                PushStatic(variableName);
                return (ExecutionResult.Success, null);
            }
        }
        else if (variablePrefix == VariablePrefix.Array || variablePrefix == VariablePrefix.ArrayPopAF || variablePrefix == VariablePrefix.ArrayPushAF)
        {
            if (variablePrefix == VariablePrefix.Array)
            {
                // Older (pre-GMS2) bytecode doesn't always tag array-style variable references with
                // a precise owner context at conversion time (see GameConverter.cs's variable-type
                // parsing) -- it comes through as a literal "undefined" context, decoded as
                // VariableType.None, for variables that ARE genuinely self/global/local/builtin/
                // instance-owned; the actual owner is still correctly resolved at runtime via the
                // popped instanceId below (same stack shape either way), so None just needs to be
                // handled exactly like Self rather than as its own separate case.
                if (variableType == VariableType.Self || variableType == VariableType.None)
                {
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();

                    if (instanceId == GMConstants.stacktop)
                    {
                        var context = Call.Stack.Pop(VMType.v);

                        if (context is GMLObject obj)
                        {
                            PushSelfArrayIndex(obj, variableName, index);
                            return (ExecutionResult.Success, null);
                        }
                        else
                        {
                            instanceId = context.Conv<int>();
                        }
                    }

                    if (instanceId == GMConstants.global)
                    {
                        PushGlobalArrayIndex(variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.local)
                    {
                        PushLocalArrayIndex(variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.argument)
                    {
                        if (variableName == "argument")
                        {
                            PushArgument(index);
                        }
                        else
                        {
                            PushArgumentArrayIndex(variableName, index);
                        }

                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.self)
                    {
                        PushSelfArrayIndex(Self.Self, variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.builtin)
                    {
                        PushBuiltinArrayIndex(variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.other)
                    {
                        PushSelfArrayIndex(Other.Self, variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                    else
                    {
                        if (instanceId < GMConstants.FIRST_INSTANCE_ID)
                        {
                            // asset id
                            var self = InstanceManager.FindByAssetId(instanceId).MinBy(x => x.instanceId)!;

                            if (self == null)
                            {
                                DebugLog.LogError($"Couldn't find any instances of {AssetIndexManager.GetName(AssetType.objects, instanceId)}");
                                Call.Stack.Push(null, VMType.v);
                                return (ExecutionResult.Success, null);
                            }

                            PushSelfArrayIndex(self, variableName, index);
                            return (ExecutionResult.Success, null);
                        }
                        else
                        {
                            // instance id
                            var self = InstanceManager.FindByInstanceId(instanceId)!;
                            PushSelfArrayIndex(self, variableName, index);
                            return (ExecutionResult.Success, null);
                        }
                    }

                    //return (ExecutionResult.Failed, $"Don't know how to push {instruction.Raw} index:{index} instanceid:{instanceId}");
                }
                else if (variableType == VariableType.Global)
                {
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();

                    if (instanceId == GMConstants.global)
                    {
                        PushGlobalArrayIndex(variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                }
                else if (variableType == VariableType.Local)
                {
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();

                    if (instanceId == GMConstants.local)
                    {
                        PushLocalArrayIndex(variableName, index);
                        return (ExecutionResult.Success, null);
                    }
                }
                else if (variableType == VariableType.BuiltIn)
                {
                    // Array-style built-ins with no owning instance (view_xview[0], view_hview[0],
                    // etc, resolved via VariableResolver.BuiltInVariables) -- same instruction shape
                    // as the Global/Local array-push cases above (index then a now-unneeded owner
                    // placeholder), just never routed anywhere before. (Older bytecode that tags
                    // these as VariableType.None instead is caught by the broadened Self/None branch
                    // above, since PushSelfArrayIndex already checks BuiltInVariables first
                    // regardless of which instance context it's given.)
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    Call.Stack.Pop(VMType.i);

                    PushBuiltinArrayIndex(variableName, index);
                    return (ExecutionResult.Success, null);
                }
            }
            else if (variablePrefix == VariablePrefix.ArrayPopAF)
            {
                if (variableType == VariableType.Self)
                {
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();

                    // TODO: make into methods and move out duplicated code
                    if (instanceId == GMConstants.global)
                    {
                        VariableResolver.ArraySet(index, new List<object?>(),
                            () => VariableResolver.GlobalVariables.GetValueOrDefault(variableName),
                            array => VariableResolver.GlobalVariables[variableName] = array,
                            onlyGrow: true);

                        var array = VariableResolver.GlobalVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.local)
                    {
                        VariableResolver.ArraySet(index, new List<object?>(),
                            () => Call.Locals.GetValueOrDefault(variableName),
                            array => Call.Locals[variableName] = array,
                            onlyGrow: true);

                        var array = Call.Locals[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.self)
                    {
                        // TODO: check builtin self var
                        VariableResolver.ArraySet(index, new List<object?>(),
                            () => Self.Self.SelfVariables.GetValueOrDefault(variableName),
                            array => Self.Self.SelfVariables[variableName] = array,
                            onlyGrow: true);

                        var array = Self.Self.SelfVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                }
            }
            else if (variablePrefix == VariablePrefix.ArrayPushAF)
            {
                if (variableType == VariableType.Self)
                {
                    var index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();

                    if (instanceId == GMConstants.global)
                    {
                        var array = VariableResolver.GlobalVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.local)
                    {
                        var array = Call.Locals[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.self)
                    {
                        // TODO: check builtin self var
                        var array = Self.Self.SelfVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId == GMConstants.stacktop)
                    {
                        var array = FetchSelf(Call.Stack.Pop(VMType.v))!.SelfVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                    else if (instanceId < 0)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        var inst = InstanceManager.Find(instanceId);

                        if (inst == null)
                        {
                            DebugLog.LogError($"Tried to push variable {variableName} from {instanceId}, which doesn't exist!!");

                            DebugLog.PrintCallStack(DebugLog.LogType.Error);

                            DebugLog.LogError(Environment.StackTrace);

                            Call.Stack.Push(null, VMType.v);
                            return (ExecutionResult.Failed, null);
                        }

                        var array = inst.SelfVariables[variableName].Conv<IList>();
                        Call.Stack.Push(SafeArrayIndex(array, index, variableName), VMType.v);
                        return (ExecutionResult.Success, null);
                    }
                }
            }
        }
        else if (variablePrefix == VariablePrefix.Stacktop)
        {
            // Same pre-GMS2 "undefined owner context" quirk noted above for the Array prefix --
            // treat None exactly like Self here too.
            if (variableType == VariableType.Self || variableType == VariableType.None)
            {
                var id = Call.Stack.Pop(VMType.i).Conv<int>();

                if (id == GMConstants.stacktop)
                {
                    var popped = Call.Stack.Pop(VMType.v);

                    if (popped is GMLObject gmlo)
                    {
                        PushSelf(gmlo, variableName);
                        return (ExecutionResult.Success, null);
                    }
                    else
                    {
                        id = popped.Conv<int>();
                    }
                }
                else if (id == GMConstants.other)
                {
                    PushOther(variableName);
                    return (ExecutionResult.Success, null);
                }

                PushIndex(id, variableName);
                return (ExecutionResult.Success, null);
            }
        }

        return (ExecutionResult.Failed, $"Don't know how to push {instruction.Raw}");
    }
}
