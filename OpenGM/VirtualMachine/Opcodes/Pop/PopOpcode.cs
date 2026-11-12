using OpenGM.IO;
using OpenGM.SerializedFiles;
using System.Collections;

namespace OpenGM.VirtualMachine;

public static partial class VMExecutor
{
    // Storing a value into a variable is how a second lasting reference to a pre-2.3 GML array comes
    // into existence, which is what makes the array copy-on-write from then on (see GMLArray). Every
    // whole-value store goes through one of the PopTo* methods below, so that's where it's flagged.

    public static void PopToGlobal(string varName, object? value)
    {
        GMLArray.MarkShared(value);
        VariableResolver.GlobalVariables[varName] = value;
    }

    public static void PopToLocal(string varName, object? value)
    {
        GMLArray.MarkShared(value);
        //Ctx.Locals[varName] = value;
        Call.Locals[varName] = value;
    }

    public static void PopToGlobalArray(string varName, int index, object? value)
    {
        VariableResolver.ArraySet(
            index,
            value,
            () => VariableResolver.GlobalVariables.GetValueOrDefault(varName),
            array => VariableResolver.GlobalVariables[varName] = array);
    }

    public static void PopToLocalArray(string varName, int index, object? value)
    {
        VariableResolver.ArraySet(
            index,
            value,
            () => Call.Locals.GetValueOrDefault(varName),
            array => Call.Locals[varName] = array);
    }

    public static void PopToSelf(IStackContextSelf self, string varName, object? value)
    {
        GMLArray.MarkShared(value);

        // check built in variables beforehand
        if (VariableResolver.BuiltInVariables.ContainsKey(varName))
        {
            VariableResolver.BuiltInVariables[varName].setter!(value, 0);
            return;
        }

        if (VariableResolver.BuiltInSelfVariables.TryGetValue(varName, out var gettersetter) && self is GamemakerObject gm)
        {
            gettersetter.setter!(gm, value);
        }
        else
        {
            self.SelfVariables[varName] = value;
        }
    }

    public static void PopToSelfArray(IStackContextSelf self, string varName, int index, object? value)
    {
        // The write-side twin of the argumentN case in PushSelfArrayIndex, and worse: argumentN's
        // registered setter is `(val, _) => PopToArgument(0, val)`, which discards the index too --
        // so `argument0[2] = v` handed to the built-in table doesn't write element 2, it replaces
        // the whole of argument 0 with v. Index into its value instead, growing it like any other
        // array (ArraySet), and write the result back as that argument.
        if (TryGetArgumentIndex(varName, out var argIndex))
        {
            var arguments = (object?[])Call.Locals["arguments"].Conv<IList>();
            if (argIndex >= arguments.Length)
            {
                return; // no such argument was passed; nothing to index into
            }

            VariableResolver.ArraySet(
                index,
                value,
                () => arguments[argIndex],
                array => arguments[argIndex] = array);
            return;
        }

        if (VariableResolver.BuiltInVariables.TryGetValue(varName, out var gs))
        {
            if (gs.setter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            gs.setter(value, index);
        }
        else if (VariableResolver.BuiltInSelfVariables.TryGetValue(varName, out var gsSelf) && self is GamemakerObject gm)
        {
            if (gsSelf.getter == null || gsSelf.setter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            VariableResolver.ArraySet(
                index,
                value,
                () => gsSelf.getter(gm),
                array => gsSelf.setter(gm, array));
        }
        else
        {
            VariableResolver.ArraySet(
                index,
                value,
                () => self.SelfVariables.GetValueOrDefault(varName),
                array => self.SelfVariables[varName] = array);
        }
    }

    /// <summary>
    /// The array-subscripted twin of <see cref="PopToIndex"/>: <c>obj.arr[i] = v</c> where the
    /// owner is an object index, not an instance id.
    ///
    /// Assigning through an object index writes to EVERY instance of that object, and the array
    /// subscript changes nothing about that -- GameMaker resolves the owner first and applies the
    /// store to each instance it names (OpenGMK's <c>Instruction::SetVariable</c> hands the same
    /// <c>array_index</c> to <c>set_instance_var</c> once per instance of <c>Target::Objects</c>).
    /// The plain-variable path here has always done that; the array path instead picked the
    /// lowest-id instance and wrote only to it, so the two disagreed about the same statement
    /// depending only on whether it had a subscript.
    ///
    /// I Wanna Spook Jam's headless-horseman phase is the visible case: its controller arms all
    /// eight orbiting pumpkins to fire with <c>objPolarBossHHPumpkin.alarm[0] = 1</c>, and each
    /// pumpkin's alarm spawns a cherry and re-arms itself. Writing to one instance left one pumpkin
    /// firing instead of eight, an eighth of the intended bullet density.
    /// </summary>
    public static void PopToIndexArray(int assetId, string varName, int index, object? value)
    {
        if (assetId < GMConstants.FIRST_INSTANCE_ID)
        {
            // Asset index -- every instance of the object (FindByAssetId also walks child objects,
            // and resolves the negative all/other/noone selectors).
            foreach (var instance in InstanceManager.FindByAssetId(assetId))
            {
                PopToSelfArray(instance, varName, index, value);
            }
        }
        else
        {
            var instance = InstanceManager.FindByInstanceId(assetId);

            if (instance == null)
            {
                DebugLog.LogError($"Instance {assetId} couldn't be found!");
                return;
            }

            PopToSelfArray(instance, varName, index, value);
        }
    }

    public static void PopToIndex(int assetId, string varName, object? value)
    {
        if (assetId < GMConstants.FIRST_INSTANCE_ID)
        {
            // Asset Index
            var instances = InstanceManager.FindByAssetId(assetId);

            foreach (var instance in instances)
            {
                if (instance == null)
                {
                    throw new NotImplementedException();
                }

                PopToSelf(instance, varName, value);
            }
        }
        else
        {
            // Instance Id
            var instance = InstanceManager.FindByInstanceId(assetId);

            if (instance == null)
            {
                throw new NotImplementedException($"Instance {assetId} couldn't be found!");
            }

            // TODO : double check this is always self. might be local as well????
            PopToSelf(instance, varName, value);
        }
    }

    public static void PopToArgument(int index, object? value)
    {
        GMLArray.MarkShared(value);

        var args = (object?[])Call.Locals["arguments"].Conv<IList>();

        if (index >= args.Length) // could change ExecuteCode to make args a list, but its fine
        {
            Array.Resize(ref args, index + 1);
        }

        args[index] = value;
        Call.Locals["arguments"] = args;
    }

    public static void PopToOther(string varName, object? value)
    {
        PopToSelf(Other.Self, varName, value);
    }

    public static void PopToBuiltIn(string varName, object? value)
    {
        if (!VariableResolver.BuiltInVariables.TryGetValue(varName, out var builtinGetSet))
        {
            // "Welcome to hell" - colin
            // At some point, self started only being used when explicitly doing "self.", and builtin is used instead.
            PopToSelf(Self.Self, varName, value);
        }
        else
        {
            builtinGetSet.setter!(value, 0);
        }
    }

    public static void PopToBuiltInArray(string varName, int index, object? value)
    {
        if (VariableResolver.BuiltInVariables.TryGetValue(varName, out var gettersetter))
        {
            if (gettersetter.setter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            gettersetter.setter(value, index);
        }
        // Same `is GamemakerObject` guard as PushBuiltin: an instance builtin only means anything on
        // an instance, and a struct member is free to share the name.
        else if (VariableResolver.BuiltInSelfVariables.TryGetValue(varName, out var selfgettersetter)
                 && Self.Self is GamemakerObject)
        {
            if (selfgettersetter.getter == null || selfgettersetter.setter == null)
            {
                throw new NotImplementedException($"Built in variable {varName} not implemented.");
            }

            VariableResolver.ArraySet(
                index,
                value,
                () => selfgettersetter.getter(Self.GMSelf),
                array => selfgettersetter.setter(Self.GMSelf, array));
        }
        else
        {
            // try this? idk
            PopToSelfArray(Self.Self, varName, index, value);
            //throw new ArgumentException($"No such builtin array \"{varName}\"");
        }
    }

    public static void PopToStatic(string varName, object? value)
    {
        GMLArray.MarkShared(value);

        var currentFunc = Call.Function;

        if (currentFunc == null)
        {
            // uhhhhh fuckin uhhh
            throw new NotImplementedException();
        }

        if (currentFunc.StaticVariables == null)
        {
            currentFunc.StaticVariables = new();
        }

        // TODO: this check breaks it? weird

        /*if (!currentFunc.StaticVariables.ContainsKey(varName) && currentFunc.HasStaticInitRan)
        {
            throw new NotImplementedException("StaticVariables should contain every static variable after initialization!?");
        }*/

        currentFunc.StaticVariables[varName] = value;
    }

    public static (ExecutionResult, object?) DoPop(VMCodeInstruction instruction)
    {
        if (instruction.TypeOne == VMType.e)
        {
            // weird swap thingy
            throw new NotImplementedException();
        }

        var variableName = instruction.variableName;
        var variableType = instruction.variableType;
        var variablePrefix = instruction.variablePrefix;
        var assetId = instruction.assetId;

        if (variablePrefix == VariablePrefix.None)
        {
            // we're just popping to a normal variable. thank god.
            var dataPopped = Call.Stack.Pop(instruction.TypeTwo);

            if (variableType == VariableType.Global)
            {
                PopToGlobal(variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Local)
            {
                PopToLocal(variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Self)
            {
                // A global init script runs with no instance, and GM's own "self" there is the
                // global scope -- so a bare `x = 5` at that level really is global.x = 5, which is
                // what this redirect is for.
                //
                // It must be conditional on there being no self, though, NOT on the global-init
                // phase alone: that phase also runs every struct literal and constructor a global
                // script builds, and those DO have a self -- the object being constructed. Sending
                // their field writes to globals left every such struct empty. tijit engine 2 builds
                // its settings struct and all its input binds this way, so `gameSetting("volumeX")`
                // answered undefined for the whole run and its init died dividing by it.
                if (GlobalInit && Self.Self is null)
                {
                    PopToGlobal(variableName, dataPopped);
                    return (ExecutionResult.Success, null);
                }

                PopToSelf(Self.Self, variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Index)
            {
                PopToIndex(assetId, variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Argument)
            {
                var strIndex = variableName[8..]; // skip "argument"
                var index = int.Parse(strIndex);
                PopToArgument(index, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.BuiltIn)
            {
                PopToBuiltIn(variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Other)
            {
                PopToOther(variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Static)
            {
                PopToStatic(variableName, dataPopped);
                return (ExecutionResult.Success, null);
            }
        }
        else if (variablePrefix == VariablePrefix.Array || variablePrefix == VariablePrefix.ArrayPopAF || variablePrefix == VariablePrefix.ArrayPushAF)
        {
            // pop appears to not support ArrayPopAF or ArrayPushAF

            if (variablePrefix == VariablePrefix.Array)
            {
                int index;
                //int instanceId;
                object? context;
                object? value;
                if (instruction.TypeOne == VMType.i) // flips value and id pop
                {
                    value = Call.Stack.Pop(instruction.TypeTwo);

                    index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();
                    if (instanceId == GMConstants.stacktop)
                    {
                        context = Call.Stack.Pop(VMType.v);
                    }
                    else
                    {
                        context = instanceId;
                    }
                }
                else // v
                {
                    index = Call.Stack.Pop(VMType.i).Conv<int>();
                    var instanceId = Call.Stack.Pop(VMType.i).Conv<int>();
                    if (instanceId == GMConstants.stacktop)
                    {
                        context = Call.Stack.Pop(VMType.v);
                    }
                    else
                    {
                        context = instanceId;
                    }

                    value = Call.Stack.Pop(instruction.TypeTwo);
                }

                // Older (pre-GMS2) bytecode doesn't always tag array-style variable references with
                // a precise owner context at conversion time (see GameConverter.cs's variable-type
                // parsing) -- it comes through as a literal "undefined" context, decoded as
                // VariableType.None, for variables that ARE genuinely self/global/local/builtin/
                // instance-owned; the actual owner is still correctly resolved at runtime via the
                // popped instanceId/context value below (same stack shape either way), so None just
                // needs to be handled exactly like Self rather than as its own separate case.
                if (variableType == VariableType.Self || variableType == VariableType.None)
                {
                    if (context is GMLObject obj)
                    {
                        PopToSelfArray(obj, variableName, index, value);
                        return (ExecutionResult.Success, null);
                    }
                    else
                    {
                        var instanceId = context.Conv<int>();
                        if (instanceId == GMConstants.global)
                        {
                            PopToGlobalArray(variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                        else if (instanceId == GMConstants.local)
                        {
                            PopToLocalArray(variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                        else if (instanceId == GMConstants.self)
                        {
                            PopToSelfArray(Self.Self, variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                        else if (instanceId == GMConstants.builtin)
                        {
                            PopToBuiltInArray(variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                        else
                        {
                            PopToIndexArray(instanceId, variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                    }
                }
                else if (variableType == VariableType.Global)
                {
                    if (context is GMLObject obj)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        var instanceId = context.Conv<int>();
                        if (instanceId == GMConstants.global)
                        {
                            PopToGlobalArray(variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                    }
                }
                else if (variableType == VariableType.Local)
                {
                    if (context is GMLObject obj)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        var instanceId = context.Conv<int>();
                        if (instanceId == GMConstants.local)
                        {
                            PopToLocalArray(variableName, index, value);
                            return (ExecutionResult.Success, null);
                        }
                    }
                }
                else if (variableType == VariableType.BuiltIn)
                {
                    // Array-style built-ins with no owning instance (view_xview[0], view_hview[0],
                    // etc), same shape as the Global/Local cases above -- just never routed anywhere
                    // before. (Older bytecode that tags these as VariableType.None instead is caught
                    // by the broadened Self/None branch above, since PopToSelfArray already checks
                    // BuiltInVariables first regardless of which instance context it's given.)
                    PopToBuiltInArray(variableName, index, value);
                    return (ExecutionResult.Success, null);
                }

                return (ExecutionResult.Failed, $"Don't know how to execute {instruction.Raw} (index={index}, context={context}, value={value})");
            }
        }
        else if (variablePrefix == VariablePrefix.Stacktop)
        {
            int id;
            object? value;

            if (instruction.TypeOne == VMType.i) // flips value and id pop
            {
                value = Call.Stack.Pop(instruction.TypeTwo);

                id = Call.Stack.Pop(VMType.i).Conv<int>();
                if (id == GMConstants.stacktop)
                {
                    var popped = Call.Stack.Pop(VMType.v);

                    if (popped is GMLObject gmlo)
                    {
                        PopToSelf(gmlo, variableName, value);
                        return (ExecutionResult.Success, null);
                    }
                    else
                    {
                        id = popped.Conv<int>();
                    }
                }
            }
            else // v
            {
                id = Call.Stack.Pop(VMType.i).Conv<int>();
                if (id == GMConstants.stacktop)
                {
                    var popped = Call.Stack.Pop(VMType.v);

                    if (popped is GMLObject gmlo)
                    {
                        value = Call.Stack.Pop(instruction.TypeTwo);
                        PopToSelf(gmlo, variableName, value);
                        return (ExecutionResult.Success, null);
                    }
                    else
                    {
                        id = popped.Conv<int>();
                    }
                }

                value = Call.Stack.Pop(instruction.TypeTwo);
            }

            // TODO: do we even need to check variabletype?
            // Same pre-GMS2 "undefined owner context" quirk as the Array-prefix case above
            // (GameConverter.cs's variable-type parsing decodes an untagged "undefined" context as
            // VariableType.None) -- the owner is still correctly resolved at runtime via the popped
            // id below, so None just needs to be treated exactly like Self here too.
            if (variableType == VariableType.Self || variableType == VariableType.None)
            {
                if (id == GMConstants.global)
                {
                    PopToGlobal(variableName, value);
                    return (ExecutionResult.Success, null);
                }
                else if (id == GMConstants.self)
                {
                    if (Self.Self == null)
                    {
                        // for global scripts
                        PopToGlobal(variableName, value);
                    }
                    else
                    {
                        PopToSelf(Self.Self, variableName, value);
                    }

                    return (ExecutionResult.Success, null);
                }
                else if (id == GMConstants.noone)
                {
                    // uh what the fuck
                    DebugLog.LogWarning($"Tried to pop {value} into {variableName} on no object???");
                    return (ExecutionResult.Success, null);
                }
                else if (id == GMConstants.builtin)
                {
                    if (Self.Self == null)
                    {
                        // for global scripts
                        PopToGlobal(variableName, value);
                        return (ExecutionResult.Success, null);
                    }

                    DebugLog.Log($"Set {variableName} on {Self.GMSelf.Definition.Name} to {value}");
                    PopToSelf(Self.Self, variableName, value);

                    return (ExecutionResult.Success, null);
                }

                PopToIndex(id, variableName, value);
                return (ExecutionResult.Success, null);
            }
            else if (variableType == VariableType.Global)
            {
                if (id == GMConstants.global)
                {
                    PopToGlobal(variableName, value);
                    return (ExecutionResult.Success, null);
                }
            }
            else if (variableType == VariableType.Static)
            {
                if (id == GMConstants.@static)
                {
                    // TODO: do proper static stuff here
                    // static variables are global per function definition
                    PopToGlobal($"static {Call.CodeName} {variableName}", value);
                    return (ExecutionResult.Success, null);
                }
            }
        }

        return (ExecutionResult.Failed, $"Don't know how to execute {instruction.Raw}");
    }
}
