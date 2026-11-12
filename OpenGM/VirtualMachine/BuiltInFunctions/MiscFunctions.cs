using OpenGM.IO;
using OpenGM.Rendering;
using UndertaleModLib.Models;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class MiscFunctions
    {
        // Real async HTTP networking (request/response via the Async - HTTP event) isn't
        // implemented -- registered as a safe stub so a script that calls it (e.g. an update
        // checker) doesn't abort entirely partway through, same reasoning as any other stubbed
        // extension function. -1 matches the real function's own "request failed to start" return
        // value, so code checking the result before using it stays safe.
        [GMLFunction("http_get", GMLFunctionFlags.Stub)]
        public static object? http_get(object?[] args) => -1;

        // See SpriteManager.screen_save_part's own doc comment -- captures pixels into an in-memory
        // cache rather than actually encoding and writing a PNG (no encoder is wired up, only
        // StbImageSharp's decoder), which sprite_add checks before touching disk.
        [GMLFunction("screen_save_part")]
        public static object? screen_save_part(object?[] args)
        {
            var fname = args[0].Conv<string>();
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();
            var w = args[3].Conv<int>();
            var h = args[4].Conv<int>();

            SpriteManager.screen_save_part(Entry.ResolveDataWinPath(fname), x, y, w, h);
            return null;
        }

        [GMLFunction("event_inherited")]
        public static object? event_inherited(object?[] args)
        {
            if (VMExecutor.Self.ObjectDefinition?.parent == null)
            {
                return null;
            }

            GamemakerObject.ExecuteEvent(VMExecutor.Self.GMSelf, VMExecutor.Self.ObjectDefinition.parent, VMExecutor.Call.EventType, VMExecutor.Call.EventIndex);
            return null;
        }

        [GMLFunction("event_perform")]
        public static object? event_perform(object?[] args)
        {
            var type = args[0].Conv<int>();
            var numb = args[1].Conv<int>();

            GamemakerObject.ExecuteEvent(VMExecutor.Self.GMSelf, VMExecutor.Self.ObjectDefinition, (EventType)type + 1, numb);
            return null;
        }

        /// <summary>
        /// Runs an event on self, but looked up on ANOTHER object's definition -- i.e. borrow that
        /// object's handler and run it with self as the instance. Note the event still runs on self;
        /// the object argument only decides whose code is found.
        /// </summary>
        [GMLFunction("event_perform_object")]
        public static object? event_perform_object(object?[] args)
        {
            var obj = args[0].Conv<int>();
            var type = args[1].Conv<int>();
            var numb = args[2].Conv<int>();

            if (!InstanceManager.ObjectDefinitions.TryGetValue(obj, out var definition))
            {
                DebugLog.LogError($"event_perform_object called with a non-existent object index {obj}");
                return null;
            }

            GamemakerObject.ExecuteEvent(VMExecutor.Self.GMSelf, definition, (EventType)type + 1, numb);
            return null;
        }

        // event_perform_async

        [GMLFunction("event_user")]
        public static object? event_user(object?[] args)
        {
            var numb = args[0].Conv<int>();
            GamemakerObject.ExecuteEvent(VMExecutor.Self.GMSelf, VMExecutor.Self.ObjectDefinition, EventType.Other, (int)EventSubtypeOther.User0 + numb);
            return null;
        }

        // event_perform_object

        // external_define
        // external_call
        // external_free

        [GMLFunction("window_handle")]
        public static object? window_handle(object?[] args)
        {
#if !BROWSER
            if (OperatingSystem.IsWindows())
            {
                unsafe
                {
                    // converting this to long so conversions wont fail
                    return (long)OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetWin32Window(CustomWindow.Instance.WindowPtr);
                }
            }
#endif

            DebugLog.LogWarning("window_handle not implemented for current platform.");
            return null;
        }

        // window_device

        [GMLFunction("show_debug_message")]
        public static object? show_debug_message(object?[] args)
        {
            if (args.Length == 0)
            {
                DebugLog.LogUser("undefined");
                return null;
            }

            DebugLog.LogUser((string)MathFunctions.@string(args[0]));
            return null;
        }

        // show_debug_overlay
        // debug_event

        [GMLFunction("show_error")]
        public static object? show_error(object?[] args)
        {
            var message = args[0].Conv<string>();
            var abort = args[1].Conv<bool>();

            DebugLog.LogError(message);

            if (abort)
            {
                RoomManager.EndGame();
            }

            return null;
        }
        // debug_get_callstack

        // gif_add_surface
        // gif_save
        // gif_save_buffer
        // gif_open

        [GMLFunction("alarm_set")]
        public static object? alarm_set(object?[] args)
        {
            var index = args[0].Conv<int>();
            var value = args[1].Conv<int>();

            VMExecutor.Self.GMSelf.alarm[index] = value;
            return null;
        }

        [GMLFunction("alarm_get")]
        public static object? alarm_get(object?[] args)
        {
            var index = args[0].Conv<int>();
            return VMExecutor.Self.GMSelf.alarm[index];
        }

        [GMLFunction("variable_global_exists")]
        public static object variable_global_exists(object?[] args)
        {
            var name = args[0].Conv<string>();
            return VariableResolver.GlobalVariables.ContainsKey(name);
        }

        [GMLFunction("variable_global_get")]
        public static object? variable_global_get(object?[] args)
        {
            var name = args[0].Conv<string>();
            return VariableResolver.GlobalVariables.GetValueOrDefault(name);
        }

        [GMLFunction("variable_global_set")]
        public static object? variable_global_set(object?[] args)
        {
            var name = args[0].Conv<string>();
            var value = args[1];

            VariableResolver.GlobalVariables[name] = value;
            return null;
        }

        [GMLFunction("struct_exists")]
        [GMLFunction("variable_instance_exists")]
        [GMLFunction("variable_struct_exists")]
        public static object variable_instance_exists(object?[] args)
        {
            var name = args[1].Conv<string>();

            IStackContextSelf? instance;

            if (args[0] is GMLObject)
            {
                instance = args[0].Conv<GMLObject>();
            }
            else
            {
                var instanceId = args[0].Conv<int>();

                if (instanceId == GMConstants.global)
                {
                    throw new NotImplementedException();
                }
                else if (instanceId < GMConstants.FIRST_INSTANCE_ID)
                {
                    // todo : first how?
                    var all = InstanceManager.FindByAssetId(instanceId);

                    if (all == null || all.Count == 0)
                    {
                        DebugLog.LogError($"Couldn't find any instances with asset index {instanceId}");
                        DebugLog.PrintCallStack(DebugLog.LogType.Error);
                        return false;
                    }

                    instance = all.First();
                }
                else
                {
                    instance = InstanceManager.FindByInstanceId(instanceId);
                }
            }

            if (instance == null)
            {
                DebugLog.LogWarning("Couldn't find struct " + args[0]);
                return false;
            }

            if (instance.SelfVariables.ContainsKey(name))
            {
                return true;
            }

            return false;
        }

        [GMLFunction("struct_get")]
        [GMLFunction("variable_instance_get")]
        [GMLFunction("variable_struct_get")]
        public static object? variable_instance_get(object?[] args)
        {
            var name = args[1].Conv<string>();

            IStackContextSelf? instance;

            if (args[0] is GMLObject)
            {
                instance = args[0].Conv<GMLObject>();
            }
            else
            {
                var instanceId = args[0].Conv<int>();

                if (instanceId == GMConstants.global)
                {
                    throw new NotImplementedException();
                }
                else if (instanceId < GMConstants.FIRST_INSTANCE_ID)
                {
                    // todo : first how?
                    var all = InstanceManager.FindByAssetId(instanceId);

                    if (all == null || all.Count == 0)
                    {
                        DebugLog.LogError($"Couldn't find any instances with asset index {instanceId}");
                        DebugLog.PrintCallStack(DebugLog.LogType.Error);
                        return null;
                    }

                    instance = all.First();
                }
                else
                {
                    instance = InstanceManager.FindByInstanceId(instanceId);
                }
            }

            if (instance == null)
            {
                return null;
            }

            if (VariableResolver.BuiltInSelfVariables.ContainsKey(name))
            {
                var (getter, setter) = VariableResolver.BuiltInSelfVariables[name];

                if (getter == null)
                {
                    throw new NotImplementedException($"Built in variable {name} not implemented.");
                }

                return getter((GamemakerObject)instance);
            }

            return instance.SelfVariables.TryGetValue(name, out var value) ? value : null;
        }

        [GMLFunction("struct_set")]
        [GMLFunction("variable_instance_set")]
        [GMLFunction("variable_struct_set")]
        public static object? variable_instance_set(object?[] args)
        {
            //var instanceId = args[0].Conv<int>();
            var name = args[1].Conv<string>();
            var value = args[2];

            if (args[0] is GMLObject gmlObj)
            {
                gmlObj.SelfVariables[name] = value;
                return null;
            }

            var instanceId = args[0].Conv<int>();

            if (instanceId == GMConstants.global)
            {
                throw new NotImplementedException();
            }
            else if (instanceId < GMConstants.FIRST_INSTANCE_ID)
            {
                // asset id
                // TODO : does this actually iterate? html seems to, but not sure about c++
                var instances = InstanceManager.FindByAssetId(instanceId);

                foreach (var instance in instances)
                {
                    if (VariableResolver.BuiltInSelfVariables.TryGetValue(name, out var getset))
                    {
                        var (getter, setter) = getset;
                        setter?.Invoke(instance, value);

                        return null;
                    }

                    instance.SelfVariables[name] = value;
                }
            }
            else
            {
                // instance id
                var instance = InstanceManager.FindByInstanceId(instanceId);

                if (instance == null)
                {
                    DebugLog.LogError($"No instance of id {instanceId}");
                    return null;
                }

                if (VariableResolver.BuiltInSelfVariables.TryGetValue(name, out var getset))
                {
                    var (getter, setter) = getset;
                    setter?.Invoke(instance, value);

                    return null;
                }

                instance.SelfVariables[name] = value;
            }

            return null;
        }

        // variable_instance_get_names
        // variable_instance_names_count
        // variable_struct_exists
        // variable_struct_get
        // variable_struct_set
        // variable_struct_set_pre
        // variable_struct_set_post

        [GMLFunction("struct_get_names")]
        [GMLFunction("variable_struct_get_names")]
        public static object? variable_struct_get_names(object?[] args)
        {
            var @struct = args[0].Conv<GMLObject>();

            var varNames = new List<string>();
            foreach (var (name, val) in @struct.SelfVariables)
            {
                varNames.Add(name);
            }

            return varNames;
        }

        [GMLFunction("struct_names_count")]
        [GMLFunction("variable_struct_names_count")]
        public static object? variable_struct_names_count(object?[] args)
        {
            return args[0].Conv<GMLObject>().SelfVariables.Count;
        }

        // variable_struct_remove
        // gc_collect
        // gc_enable
        // gc_is_enabled
        // gc_get_stats
        // gc_target_frame_time
        // gc_get_target_frame_time
        // clipboard_has_text
        // clipboard_set_text
        // clipboard_get_text

        [GMLFunction("url_open", GMLFunctionFlags.Stub)]
        [GMLFunction("url_open_ext", GMLFunctionFlags.Stub)]
        [GMLFunction("url_open_full", GMLFunctionFlags.Stub)]
        public static object? url_open(object?[] args)
        {
            var url = args[0].Conv<string>();

            // Registered rather than implemented on purpose: the engine runs in a Web Worker, which
            // can't open a window itself, so this would need a whole new worker -> main thread
            // bridge. Not worth that for what games use it for (a credits link), and an unresolved
            // call would take the rest of the event with it.
            DebugLog.LogWarning($"url_open({url}) ignored -- opening a URL isn't wired up.");
            return null;
        }

        // The whole date/time family lives in DateFunctions.cs. It used to be here, built on a
        // hand-rolled JS-Date lookalike (Date.cs) whose own header called itself lazy and asked to
        // be removed; date_datetime_string returned that class's *type name*, and date_get_week
        // returned day_of_year/7 rather than an ISO week number.
        // date_set_timezone / date_get_timezone are still unimplemented -- GM only offers UTC vs
        // local, and everything here reads local time already.
        // game_set_speed

        [GMLFunction("game_get_speed")]
        public static object game_get_speed(object?[] args)
        {
            var type = args[0].Conv<int>();

            if (type == 0)
            {
                // FPS
                return Entry.GameSpeed;
            }
            else
            {
                // microseconds per frame
                return CustomMath.FloorToInt(1000000.0 / Entry.GameSpeed);
            }
        }

        // extension_get_string
    }
}
