using OpenGM.IO;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using UndertaleModLib.Models;
using EventType = OpenGM.VirtualMachine.EventType;

namespace OpenGM;

// this could also be in ObjectDefinition. it dont matter
public class ObjectDictEntry
{
    public List<GamemakerObject> Instances = new();
    public List<int> ChildrenIndexes = new();
}

public static class InstanceManager
{
    public static Dictionary<int, GamemakerObject> instances = new();
    public static Dictionary<int, GamemakerObject> inactiveInstances = new();
    public static Dictionary<int, ObjectDefinition> ObjectDefinitions = new();

    /// <summary>
    /// Every currently-registered instance with solid == true -- kept in sync by
    /// GamemakerObject.solid's setter (toggling at runtime) and RemoveInstance below (destruction,
    /// which doesn't go through that setter). See CollisionManager.IsCollidingAtCurrentPosition,
    /// the only reader.
    /// </summary>
    public static readonly HashSet<GamemakerObject> SolidInstances = new();

    public static IEnumerable<GamemakerObject> allInstances =>
        InstanceManager.instances
            .Concat(InstanceManager.inactiveInstances)
            .Select(kv => kv.Value);

    public static int NextInstanceID;

    /// <summary>
    /// Wipes every instance-tracking collection at once, SolidInstances included -- a blanket
    /// `instances.Clear()` alone leaves any solid instance's reference dangling in SolidInstances
    /// forever (it only gets cleaned up per-instance, via RemoveInstance/the solid setter), which
    /// either leaks memory or -- worse -- lets a defunct instance from a previous game/test still
    /// show up as a collision candidate in whatever comes next.
    /// </summary>
    public static void ClearAllInstances()
    {
        instances.Clear();
        inactiveInstances.Clear();
        SolidInstances.Clear();
    }

    /// <summary>
    /// for faster lookup when finding instances by asset id.
    /// gamemaker does something similar and this was a bottleneck in one of nebula's test programs.
    /// </summary>
    public static Dictionary<int, ObjectDictEntry> ObjectMap = new();

    /// <summary>
    /// <see cref="ObjectMap"/>'s keys, ascending. Fixed for the life of the game (ObjectMap gains
    /// no keys after <see cref="InitObjectMap"/>; only the per-object instance lists change), so
    /// it's built once here rather than re-sorted by every event pass that walks objects in index
    /// order -- see DrawManager.EventDispatchOrder.
    /// </summary>
    public static int[] SortedObjectIndices = Array.Empty<int>();

    public static void InitObjectMap()
    {
        ObjectMap.Clear();

        foreach (var item in ObjectDefinitions)
        {
            var entry = new ObjectDictEntry
            {
                ChildrenIndexes = ObjectDefinitions
                    .Where(x => x.Value.parent == item.Value)
                    .Select(x => x.Key).ToList()
            };

            ObjectMap.Add(item.Key, entry);
        }

        SortedObjectIndices = ObjectMap.Keys.Order().ToArray();

        // Rebuilding drops every per-object instance list, and this doesn't only run at load:
        // object_set_parent calls it again mid-game to re-derive ChildrenIndexes. Re-file what's
        // live, or that one call silently empties every list in the game. It used to merely break
        // by-object lookups (instance_exists, with (obj), collision candidates); now that the step
        // loop dispatches off these lists too (DrawManager.EventDispatchOrder), it would stop the
        // game dead. Creation order is `instances`' own insertion order, which is what the lists
        // hold and what dispatch order within an object means.
        foreach (var instance in instances.Values)
        {
            // Deactivated instances live in inactiveInstances and are deliberately absent from
            // ObjectMap until they're reactivated -- see RoomContainer.DeactivateInstance.
            ObjectMap[instance.Definition.AssetId].Instances.Add(instance);
        }
    }

    /// <summary>
    /// Adds an instance to the instance pool. This does not register it
    /// with DrawManager; use <see cref="GamemakerObject.Register"/> for
    /// this purpose.
    /// </summary>
    public static void AddInstance(GamemakerObject obj)
    {
        if (instances.ContainsKey(obj.instanceId))
        {
            DebugLog.LogWarning($"Tried to register another object with instanceId:{obj.instanceId}\nExisting:{instances[obj.instanceId].Definition.Name}\nNew:{obj.Definition.Name}");
            DebugLog.PrintCallStack(DebugLog.LogType.Warning);

            return;
        }

        if (obj == null)
        {
            DebugLog.LogError($"Tried to register a null instance!");
            return;
        }

        if (obj.Definition == null)
        {
            DebugLog.LogError($"Tried to register an instance with no definition! obj:{obj}");
            return;
        }

        instances.Add(obj.instanceId, obj);

        // Belt-and-suspenders alongside GamemakerObject.solid's setter: this path also covers
        // ReactivateInstance, which re-registers an already-existing instance (obj.solid isn't
        // being assigned there, so the setter's own tracking wouldn't fire).
        if (obj.solid)
        {
            SolidInstances.Add(obj);
        }

        var entry = ObjectMap[obj.Definition.AssetId];
        entry.Instances.Add(obj);
    }

    /// <summary>
    /// Removes an instance to the instance pool. This does not remove it
    /// from DrawManager; use <see cref="GamemakerObject.Unregister"/> for
    /// this purpose.
    /// </summary>
    public static void RemoveInstance(GamemakerObject obj)
    {
        if (obj == null)
        {
            DebugLog.LogError($"Tried to unregister a null instance!");
            return;
        }

        // An inactive instance (deactivated via instance_deactivate_object/_all) lives in
        // inactiveInstances, not instances -- destroying one (e.g. a non-persistent room's end-of-room
        // cleanup) must remove it from there too, or it leaks forever: its instanceId slot stays
        // occupied, so any future instance that reuses that same id (room-placed instances keep a
        // fixed baked id across every reload of that room, e.g. via room_restart) collides on
        // DeactivateInstance's TryAdd the next time it's deactivated. Same "any set/index mirroring a
        // subset of live instances needs the same audit" class of bug as SolidInstances above.
        var removedActive = instances.Remove(obj.instanceId);
        var removedInactive = inactiveInstances.Remove(obj.instanceId);

        if (!removedActive && !removedInactive)
        {
            DebugLog.LogVerbose($"Tried to unregister a non-registered object with instanceId:{obj.instanceId}\nObject:{obj.Definition.Name}");
            DebugLog.PrintCallStack(DebugLog.LogType.Verbose);

            return;
        }

        if (obj.Definition == null)
        {
            DebugLog.LogError($"Tried to unregister an instance with no definition! obj:{obj}");
            return;
        }

        SolidInstances.Remove(obj);

        var entry = ObjectMap[obj.Definition.AssetId];
        entry.Instances.Remove(obj);
    }

    /// <summary>
    /// Whether any instance -- active or deactivated -- already holds this id. Deactivated ones
    /// count: they live in <see cref="inactiveInstances"/> rather than <see cref="instances"/>, but
    /// the id is still theirs and reusing it would collide the moment they come back.
    /// </summary>
    public static bool InstanceIdInUse(int instanceId)
        => instances.ContainsKey(instanceId) || inactiveInstances.ContainsKey(instanceId);

    /// <summary>
    /// Re-files a live instance under a different object definition, keeping <see cref="ObjectMap"/>
    /// in step. Backs <c>instance_change</c>.
    ///
    /// Swapping <see cref="GamemakerObject.Definition"/> on its own is not enough: ObjectMap is the
    /// index EVERY by-object lookup goes through (<see cref="FindByAssetId"/>, and through it
    /// <c>instance_exists</c>, <c>instance_number</c>, <c>with (obj)</c>, an <c>obj.var</c> read,
    /// and DrawManager.UpdateCollisions' candidate lists). An instance left filed under its old
    /// object is invisible as its new type and still findable as its old one -- both wrong, and in
    /// opposite directions.
    /// </summary>
    public static void ChangeObjectDefinition(GamemakerObject obj, ObjectDefinition newDefinition)
    {
        if (obj.Definition == newDefinition)
        {
            return;
        }

        // A deactivated instance isn't filed in ObjectMap at all (see RoomContainer.DeactivateInstance)
        // -- don't file it under the new object either, or reactivating it would double-add.
        var wasFiled = ObjectMap[obj.Definition.AssetId].Instances.Remove(obj);

        obj.Definition = newDefinition;

        if (wasFiled)
        {
            ObjectMap[newDefinition.AssetId].Instances.Add(obj);
        }
    }

    public static int instance_create(double x, double y, int obj)
    {
        var definition = ObjectDefinitions[obj];

        var newGM = new GamemakerObject(definition, x, y, definition.depth, NextInstanceID++, definition.sprite, definition.visible, definition.persistent, definition.textureMaskId);

        GamemakerObject.ExecuteEvent(newGM, definition, EventType.PreCreate);
        GamemakerObject.ExecuteEvent(newGM, definition, EventType.Create);
        newGM._createRan = true;
        return newGM.instanceId;
    }

    public static int instance_create_depth(double x, double y, int depth, int obj, GMLObject? var_struct)
    {
        var definition = ObjectDefinitions[obj];

        var newGM = new GamemakerObject(definition, x, y, depth, NextInstanceID++, definition.sprite, definition.visible, definition.persistent, definition.textureMaskId);

        GamemakerObject.ExecuteEvent(newGM, definition, EventType.PreCreate);

        if (var_struct != null)
        {
            foreach (var var in var_struct.SelfVariables)
            {
                VMExecutor.PopToSelf(newGM, var.Key, var.Value);
            }
        }

        GamemakerObject.ExecuteEvent(newGM, definition, EventType.Create);
        newGM._createRan = true;
        return newGM.instanceId;
    }

    public static int instance_create_layer(double x, double y, LayerContainer layer, int obj, GMLObject? var_struct)
    {
        var definition = ObjectDefinitions[obj];

        var newGM = new GamemakerObject(definition, x, y, layer.Depth, NextInstanceID++, definition.sprite, definition.visible, definition.persistent, definition.textureMaskId);
        layer.ElementsToDraw.Add(newGM);
        newGM.Layer = layer.ID;

        GamemakerObject.ExecuteEvent(newGM, definition, EventType.PreCreate);

        if (var_struct != null)
        {
            foreach (var var in var_struct.SelfVariables)
            {
                VMExecutor.PopToSelf(newGM, var.Key, var.Value);
            }
        }

        GamemakerObject.ExecuteEvent(newGM, definition, EventType.Create);
        newGM._createRan = true;
        return newGM.instanceId;
    }

    public static int instance_number(int obj)
    {
        //instances.RemoveAll(x => x == null);
        return instances.Values.Count(x => HasAssetInParents(x.Definition, obj) && x.Active && !x.Marked);
    }

    public static bool HasAssetInParents(ObjectDefinition definition, int id)
    {
        var currentDefinition = definition;
        while (currentDefinition != null)
        {
            if (currentDefinition.AssetId == id)
            {
                return true;
            }

            currentDefinition = currentDefinition.parent;
        }

        return false;
    }

    public static GamemakerObject? Find(int id, int index = 0, bool all = false)
    {
        // accidentally similar to cpp WithObjIterator. this pattern is also copy pasted throughout the codebase

        if (id == GMConstants.global)
        {
            throw new NotImplementedException("Tried to find global instance.");
        }
        else if (id == GMConstants.self)
        {
            return VMExecutor.Self.GMSelf;
        }
        else if (id == GMConstants.other)
        {
            return VMExecutor.Other.GMSelf;
        }
        else if (id < 0)
        {
            throw new NotImplementedException($"Tried to find instance with id {id}");
        }
        else if (id < GMConstants.FIRST_INSTANCE_ID)
        {
            // asset id
            var results = FindByAssetId(id, all);
            if (results.Count <= index)
            {
                return null;
            }

            return results[index];
        }
        else
        {
            // instance id
            return FindByInstanceId(id, all);
        }
    }

    public static List<GamemakerObject> FindByAssetId(int assetId, bool all = false)
    {
        var result = new List<GamemakerObject>();

        if (assetId >= 0)
        {
            AddChild(assetId);
            // TODO: find out if we can have this list sorted through other means
            result.Sort((a, b) => a.instanceId.CompareTo(b.instanceId));
            return result;
        }

        return FindByLegacyValue(assetId, all);

        void AddChild(int id)
        {
            result.AddRange(ObjectMap[id].Instances.Where(inst => !inst.Marked && !inst.Destroyed));

            if (all)
            {
                var instances = inactiveInstances
                    .Select(kv => kv.Value)
                    .Where(inst => inst.Definition.AssetId == id && !inst.Marked && !inst.Destroyed);

                result.AddRange(instances);
            }

            foreach (var child in ObjectMap[id].ChildrenIndexes)
            {
                AddChild(child);
            }
        }
    }

    public static List<GamemakerObject> FindByLegacyValue(int value, bool all = false)
    {
        var result = new List<GamemakerObject>();

        var instList = all ? allInstances : instances.Select(kv => kv.Value);

        switch (value)
        {
            case GMConstants.self:
                result.Add(VMExecutor.Self.GMSelf);
                return result;
            case GMConstants.other:
                /*var myId = VMExecutor.Self.GMSelf.instanceId;
                result.AddRange(instList.Where(inst => inst.instanceId != myId));*/
                result.Add(VMExecutor.Other.GMSelf);
                return result;
            case GMConstants.all:
                result.AddRange(instList);
                return result;
            case GMConstants.noone:
                return result;
            default:
                throw new Exception($"Tried to find instances with asset id {value}");
        }
    }

    public static GamemakerObject? FindByInstanceId(int instanceId, bool all = false)
    {
        if (instanceId == GMConstants.self)
        {
            return VMExecutor.Self.GMSelf;
        }

        if (instanceId < GMConstants.FIRST_INSTANCE_ID)
        {
            throw new Exception($"Tried to find instance by instance id {instanceId}");
        }

        var result = instances.GetValueOrDefault(instanceId);

        if (all)
        {
            result ??= inactiveInstances.GetValueOrDefault(instanceId);
        }

        return result;
    }

    public static bool instance_exists_instanceid(int instanceId)
    {
        if (!instances.ContainsKey(instanceId))
        {
            return false;
        }

        var instance = instances[instanceId];

        return instance is { Marked: false, Destroyed: false };
    }

    public static bool instance_exists_index(int assetIndex)
    {
        foreach (var (instanceId, instance) in instances)
        {
            var definition = instance.Definition;
            while (definition != null)
            {
                if (definition.AssetId == assetIndex && !instance.Marked && !instance.Destroyed)
                {
                    return true;
                }
                definition = definition.parent;
            }
        }

        return false;
    }

    /*public static void instance_destroy(GamemakerObject obj)
    {
        if (obj != null)
        {
            DebugLog.Log($"INSTANCE_DESTROY {obj.Definition.Name}");
            obj.Destroy();
            instances.Remove(obj.instanceId);
        }
    }*/

    /// <summary>
    /// An instance stays fully live for the whole of its own Destroy (and CleanUp) event, and is
    /// only marked once those have returned. GameMaker runs the event first and flags the instance
    /// afterwards -- OpenGMK's <c>instance_destroy</c> is literally
    /// <c>run_instance_event(DESTROY, ..)</c> then <c>instance_list.mark_deleted(..)</c> -- so
    /// inside that event <c>with (that_object)</c>, <c>instance_exists</c>, <c>instance_number</c>
    /// and every other lookup still see the instance being destroyed.
    ///
    /// Marking first (which is what this used to do) was a re-entrancy guard: <see cref="Marked"/>
    /// doubled as "destruction already started", so an instance that destroyed itself again from
    /// its own Destroy event didn't recurse forever. That's now a separate <see
    /// cref="GamemakerObject.Destroying"/> flag, which guards the same thing without lying about
    /// the instance's existence for the duration of the event.
    ///
    /// The difference is load-bearing for the self-counting idiom, where each of N instances holds
    /// its own copy of a countdown and the last one to die triggers something. I Wanna Spook Jam's
    /// final Polaroid boss does exactly this: each of the two purple liches starts at
    /// <c>count = 2</c>, and its Destroy event runs <c>with (objPolarBossFinalLichPurple) count -=
    /// 1</c> and then checks <c>if (count == 0)</c> on itself before spawning the next phase. With
    /// the dying instance excluded from its own <c>with</c>, the second one to die never
    /// decremented its own counter, never reached 0, and the fight simply stopped -- no error, no
    /// next phase, softlocked.
    /// </summary>
    public static void MarkForDestruction(GamemakerObject? obj, bool executeEvent)
    {
        if (obj == null)
        {
            return;
        }

        if (!obj.Marked && !obj.Destroying && obj.Active)
        {
            obj.Destroying = true;

            if (executeEvent)
            {
                GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.Destroy);
            }

            GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.CleanUp);

            obj.Marked = true;
        }
    }

    public static void ClearInactive()
    {
        inactiveInstances.Clear();
    }

    public static void ClearInstances(IEnumerable<int> toRemove)
    {
        foreach (var id in toRemove)
        {
            // toRemove can include currently-inactive instances (SavePersistentRoom saving a
            // deactivated instance), which live in inactiveInstances, not instances.
            var instance = instances.GetValueOrDefault(id) ?? inactiveInstances.GetValueOrDefault(id);
            instance?.Unregister();
        }
    }

    public static void ClearInstances(IEnumerable<GamemakerObject?> toRemove) // this doesnt need to be nullable but i dont care enough to change and test it
    {
        ClearInstances(toRemove.Where(o => o != null).Select(o => o!.instanceId));
    }

    public static void RememberOldPositions()
    {
        var instanceList = instances.Values.ToList(); // .Animate() can change instance list
        foreach (var item in instanceList)
        {
            item.xprevious = item.x;
            item.yprevious = item.y;
            item.path_previousposition = item.path_position;

            item.Animate();
        }
    }

    // The frame-wrapping pass that used to live here ran immediately after RememberOldPositions'
    // Animate() calls, so an advance and its wrap happened back to back and image_index could never
    // be observed at image_number. It's now the first thing Animate does, against the previous
    // frame's value -- see the comment there for why that one-frame delay is load-bearing.

    public static void UpdatePositions()
    {
        foreach (var (index, item) in InstanceManager.instances)
        {
            item.AdaptSpeed();

            if (item.AdaptPath())
            {
                GamemakerObject.ExecuteEvent(item, item.Definition, EventType.Other, (int)EventSubtypeOther.EndOfPath);
            }

            if (item.hspeed != 0 || item.vspeed != 0)
            {
                item.x += item.hspeed;
                item.y += item.vspeed;
                item.bbox_dirty = true;
            }
        }
    }
}
