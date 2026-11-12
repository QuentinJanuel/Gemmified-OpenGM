using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Particles;
using OpenGM.Rendering;
using OpenGM.Sequences;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Mathematics;
using UndertaleModLib.Models;
using EventType = OpenGM.VirtualMachine.EventType;

namespace OpenGM;

public static class RoomManager
{
    /// <summary>
    /// The room to change to.
    /// </summary>
    //public static Room? RoomToChangeTo = null;
    public static int New_Room;
    public static RoomContainer CurrentRoom = null!; // its set to room on start

    public static Dictionary<int, Room> RoomList = new();
    public static Dictionary<int, PersistentRoom> PersistentRooms = new();
    public static bool RoomLoaded = false;
    public static bool FirstRoom = false;

    private static void DestroyAllInstances() // this doesnt clear all the lists, but thats okay. we're closing the game or starting a new one, which clears stuff
    {
        foreach (var (instanceId, instance) in InstanceManager.instances)
        {
            instance.Destroy();
        }

        InstanceManager.ClearAllInstances();
    }

    public static void EndGame()
    {
        DestroyAllInstances();
        CustomWindow.Instance.Close();
    }

    /// <summary>
    /// game_restart: put the engine back the way it is at startup, short of re-reading the assets.
    ///
    /// This used to be three lines that jumped to a room, resetting none of the state that
    /// <see cref="Entry"/> clears on the way in -- so a restart carried the previous run's audio,
    /// globals, data structures, particle systems and persistent rooms into the new game. The one
    /// that shows is audio: the old room's music keeps playing underneath the new one's, because
    /// nothing stopped the sources and the game's own "what's playing" globals have been reset out
    /// from under it.
    /// </summary>
    public static void StartGame()
    {
        AudioManager.StopAllAudio();

        VMExecutor.EnvStack.Clear();
        VMExecutor.CallStack.Clear();

        DrawManager._drawObjects.Clear();
        DataStructuresFunctions.Clear();
        ParticleManager.Reset();
        PersistentRooms.Clear();
        VariableResolver.GlobalVariables.Clear();

        // Same ordering constraint as startup (see Entry): extension Init scripts seed globals the
        // first room's Create events read, so they have to run after the wipe rather than before it.
        ExtensionManager.Init();

        VMExecutor.GlobalInit = true;
        foreach (var item in ScriptResolver.GlobalInit)
        {
            VMExecutor.ExecuteCode(item, null);
        }
        VMExecutor.GlobalInit = false;

        InstanceManager.NextInstanceID = GameLoader.GeneralInfo.LastObjectID + 1;

        FirstRoom = true;
        // The first room in the room *order*, which isn't necessarily asset 0 -- a game whose room
        // order had been rearranged would restart into the wrong room.
        New_Room = GameLoader.GeneralInfo.RoomOrder[0];
        ChangeToWaitingRoom();
    }

    private static void SavePersistentRoom()
    {
        DebugLog.LogInfo("SAVING PERSISTENT ROOM");

        var instancesToSave = new List<GamemakerObject>();

        // events could destroy other objects, cant modify during iteration
        //
        // Deactivated instances (instance_deactivate_object/_all) sit in inactiveInstances, not
        // instances -- without including them here they'd be silently dropped: not saved (so they
        // never come back when the room reloads), not destroyed (no RoomEnd/CleanUp), just orphaned
        // in inactiveInstances forever. Same class of bug ChangeToWaitingRoom's non-persistent path
        // had for the exact same reason.
        var instanceList = new List<GamemakerObject>(InstanceManager.instances.Values)
            .Concat(InstanceManager.inactiveInstances.Values)
            .ToList();
        foreach (var instance in instanceList)
        {
            if (instance == null)
            {
                continue;
            }

            if (instance.persistent) // dont bother saving thing that is already going to be saved
            {
                continue;
            }

            // TODO : should we call RoomEnd on marked objects? will any marked objects still exist at this point?
            if (instance.Marked || instance.Destroyed)
            {
                continue;
            }

            // TODO : if RoomEnd event creates objects, should they be saved??
            GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.Other, (int)EventSubtypeOther.RoomEnd);

            instancesToSave.Add(instance);
            DrawManager.Unregister(instance);
        }

        // todo: this seems dumb and slow. do it in a not dumb way
        //InstanceManager.instances = InstanceManager.instances.Where(x => !instancesToSave.Contains(x.Value)).ToDictionary();
        InstanceManager.ClearInstances(instancesToSave);

        foreach (var item in CurrentRoom.Tiles)
        {
            DrawManager.Unregister(item);
        }

        foreach (var layer in CurrentRoom.Layers)
        {
            foreach (var element in layer.Value.ElementsToDraw)
            {
                if (element is GMTilesLayer tileLayer)
                {
                    DrawManager.Unregister(tileLayer);
                }
                else if (element is GMBackground background)
                {
                    DrawManager.Unregister(background);
                }
                else if (element is GMSprite sprite)
                {
                    DrawManager.Unregister(sprite);
                }
            }
        }

        var persistentRoom = new PersistentRoom()
        {
            RoomAssetId = CurrentRoom.AssetId,
            Container = CurrentRoom,
            Instances = instancesToSave
        };
        PersistentRooms.Add(CurrentRoom.AssetId, persistentRoom);
    }

    private static void LoadPersistentRoom(Room room, PersistentRoom value)
    {
        DebugLog.LogInfo($"LOADING PERSISTENT ROOM AssetID:{room.AssetId} ({value.Container.RoomAsset.AssetId}) Name:{value.Container.RoomAsset.Name}");
        CurrentRoom = value.Container;

        // TODO : does instance creation order affect this?
        DebugLog.Log("Instances in persistent room:");
        foreach (var instance in value.Instances)
        {
            DebugLog.Log($"{instance.instanceId} - {instance.Definition.Name} Active:{instance.Active} Marked:{instance.Marked} Destroyed:{instance.Destroyed}");

            // Route into the same dictionary the instance was saved out of -- SavePersistentRoom now
            // saves deactivated instances too (see there), and restoring one straight into `instances`
            // regardless of its own Active flag would both misreport it as active and set up the exact
            // same instanceId leak/collision this whole area was fixed for if it's deactivated again.
            if (instance.Active)
            {
                InstanceManager.instances.Add(instance.instanceId, instance);
            }
            else
            {
                InstanceManager.inactiveInstances.Add(instance.instanceId, instance);
            }

            InstanceManager.ObjectMap[instance.Definition.AssetId].Instances.Add(instance);

            // Bypasses the solid property's setter (its value isn't changing, just this instance's
            // registration state), so SolidInstances -- which SavePersistentRoom's ClearInstances
            // call correctly removed this instance from on the way out -- needs restoring directly.
            if (instance.solid)
            {
                InstanceManager.SolidInstances.Add(instance);
            }

            GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.Other, (int)EventSubtypeOther.RoomStart);

            // Deactivated instances don't draw (DeactivateInstance's Unregister() call already pulled
            // them out of DrawManager on the way out) -- only re-register the ones coming back active.
            if (instance.Active)
            {
                DrawManager.Register(instance);
            }
        }

        foreach (var tile in CurrentRoom.Tiles)
        {
            DrawManager.Register(tile);
        }

        foreach (var layer in CurrentRoom.Layers)
        {
            foreach (var element in layer.Value.ElementsToDraw)
            {
                if (element is GMTilesLayer tileLayer)
                {
                    DrawManager.Register(tileLayer);
                }
                else if (element is GMBackground background)
                {
                    DrawManager.Register(background);
                }
                else if (element is GMSprite sprite)
                {
                    DrawManager.Register(sprite);
                }
                else if (element is GMTile tile)
                {
                    DrawManager.Register(tile);
                }
            }
        }

        PersistentRooms.Remove(room.AssetId);
    }

    public static bool CheckAndChangeRoom()
    {
        CurrentRoom.HandleObjectActivation();
        if (New_Room != -1)
        {
            ChangeToWaitingRoom();
            return true;
        }

        return false;
    }

    public static void ChangeToWaitingRoom()
    {
        // TODO : What's the difference between ENDOFGAME and ABORTGAME?
        if (New_Room is GMConstants.ROOM_ENDOFGAME or GMConstants.ROOM_ABORTGAME)
        {
            EndGame();
            return;
        }
        else if (New_Room == GMConstants.ROOM_RESTARTGAME)
        {
            DestroyAllInstances();
            StartGame();
            return;
        }
        else if (New_Room == GMConstants.ROOM_LOADGAME)
        {
            throw new NotImplementedException();
        }

        var room = RoomList[New_Room];
        New_Room = -1;

        // Real GameMaker applies each room's own declared speed fresh on every room load (see
        // OpenGMK's game.rs load_room: `self.room.speed = room.speed`) -- GM8/GMS1 games have no
        // single global project fps the way GMS2 does, so this must happen here rather than once
        // at startup (see Room.cs's Speed field for why the old GeneralInfo.FPS source was wrong).
        //
        // A GMS2 room declares no speed at all and reports 0, and the project-wide FPS from GEN8 is
        // the real answer for those. Taking the 0 literally set the frame rate to zero, which does
        // not run slowly -- it never advances at all: the frame accumulator asks for 1/speed
        // seconds' worth of time before stepping. tijit engine 2 (GMS 2024) loaded, ran its first
        // room's Create and Room Start events, and then sat there forever on a black screen with
        // nothing in the log, because its own room_goto out of the init room needs a step to happen.
        Entry.SetGameSpeed(room.Speed > 0 ? room.Speed : (int)GameLoader.GeneralInfo.FPS);

        DebugLog.LogInfo($"Changing to {room!.Name}");

        if (CurrentRoom != null && CurrentRoom.Persistent)
        {
            SavePersistentRoom();
        }
        else if (CurrentRoom != null)
        {
            // remove everything that isn't persistent

            // Deactivated instances (instance_deactivate_object/_all) sit in inactiveInstances, not
            // instances -- they still need RoomEnd/CleanUp/destruction here same as active ones, or
            // they leak past the room change: room-placed instances keep a fixed baked instanceId
            // across every (re)load of that room (e.g. via room_restart), so a leaked entry collides
            // the next time a freshly-loaded instance with that same id gets deactivated.

            // events could destroy other objects, cant modify during iteration
            var instanceList = new List<GamemakerObject>(InstanceManager.instances.Values)
                .Concat(InstanceManager.inactiveInstances.Values)
                .ToList();

            foreach (var instance in instanceList)
            {
                if (instance == null)
                {
                    continue;
                }

                GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.Other, (int)EventSubtypeOther.RoomEnd);
            }

            // TODO : if RoomEnd event creates objects, should they be destroyed??
            instanceList = new List<GamemakerObject>(InstanceManager.instances.Values)
                .Concat(InstanceManager.inactiveInstances.Values)
                .ToList();

            foreach (var instance in instanceList)
            {
                if (instance == null)
                {
                    continue;
                }

                if (instance.persistent)
                {
                    continue;
                }

                // no destroy event https://forum.gamemaker.io/index.php?threads/hold-up-room-change-doesnt-trigger-destroy-event-of-objects.43007/
                GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.CleanUp);

                instance.Destroy();
            }

            foreach (var item in CurrentRoom.Tiles)
            {
                item.Destroy();
            }
            //CurrentRoom.Tiles.Clear();

            //CurrentRoom.OldBackgrounds.Clear();

            foreach (var layer in CurrentRoom.Layers)
            {
                foreach (var item in layer.Value.ElementsToDraw)
                {
                    if (item is GamemakerObject)
                    {
                        continue;
                    }

                    item.Destroy();
                }
            }
        }

        if (PersistentRooms.TryGetValue(room.AssetId, out var value))
        {
            LoadPersistentRoom(room, value);
        }
        else
        {
            RoomLoaded = false;
            CurrentRoom = new RoomContainer(room);
            OnRoomChanged();
        }
    }

    /*public static Vector2i CalculateCanvasSize()
    {
        // https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Cameras_And_Display/Cameras_And_Viewports/Cameras_And_View_Ports.htm

        // TODO : check for view_enabled here too
        if (CurrentRoom.Views.All(x => !x.Visible))
        {
            // Use room size if all views are disabled.
            // https://manual.gamemaker.io/lts/en/GameMaker_Language/GML_Reference/Cameras_And_Display/Cameras_And_Viewports/view_enabled.htm
            return new(CurrentRoom.SizeX, CurrentRoom.SizeY);
        }
    }*/

    private static void OnRoomChanged()
    {
        for (var i = 0; i < 8; i++)
        {
            var view = CurrentRoom.RoomAsset.Views[i];

            var camera = CameraManager.CreateCamera();
            camera.ViewX = view.PositionX;
            camera.ViewY = view.PositionY;
            camera.ViewWidth = view.SizeX;
            camera.ViewHeight = view.SizeY;
            camera.SpeedX = view.SpeedX;
            camera.SpeedY = view.SpeedY;
            camera.BorderX = view.BorderX;
            camera.BorderY = view.BorderY;
            camera.ViewAngle = 0;
            camera.TargetInstance = view.FollowsObject;

            camera.Build2DView(camera.ViewX + (camera.ViewWidth / 2), camera.ViewY + (camera.ViewHeight / 2));

            var runtimeView = new RuntimeView
            {
                Visible = view.Enabled,
                PortPosition = new Vector2i(view.PortPositionX, view.PortPositionY),
                PortSize = new Vector2i(view.PortSizeX, view.PortSizeY),
                Camera = camera
            };

            CurrentRoom.Views[i] = runtimeView;
        }

        var createdObjects = new List<(GamemakerObject gm, GameObject go)>();

        void RunObjEvents(GamemakerObject obj, GameObject go)
        {
            GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.PreCreate);

            var preCreateCode = GetCodeFromCodeIndex(go.PreCreateCodeID);
            if (preCreateCode != null)
            {
                VMExecutor.ExecuteCode(preCreateCode, obj, obj.Definition);
            }

            GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.Create);
            obj._createRan = true;

            var createCode = GetCodeFromCodeIndex(go.CreationCodeID);
            if (createCode != null)
            {
                VMExecutor.ExecuteCode(createCode, obj, obj.Definition);
            }
        }

        foreach (var obj in CurrentRoom.RoomAsset.GameObjects)
        {
            // "Load all instances in new room, unless they already exist due to persistence"
            // (OpenGMK's load_room, verbatim). A room's instances carry baked ids from the room
            // file and get them again on every (re)load, so a PERSISTENT instance placed in this
            // room and carried back into it would otherwise be recreated on top of the copy that's
            // still alive -- two instances, one id.
            //
            // Note this is only ever true for a persistent instance returning to its OWN room:
            // entering a DIFFERENT room that also places one creates that room's own copy, with its
            // own id, which is what lets the usual singleton guard
            // (`if (instance_number(x) > 1) instance_destroy()`) fire. Spook Jam's oZebMistController
            // is placed in 8 of the Zeb rooms and is persistent, so walking back into roomZeb1 hit
            // this: id 125108 collided, InstanceManager.AddInstance refused the registration, and
            // the half-registered instance was left running its Step event out of DrawManager's list
            // forever, scrolling the mist background in rooms that shouldn't have it.
            if (InstanceManager.InstanceIdInUse(obj.InstanceID))
            {
                continue;
            }

            var definition = InstanceManager.ObjectDefinitions[obj.DefinitionID];
            var newGM = new GamemakerObject(definition, obj.X, obj.Y, definition.depth, obj.InstanceID, definition.sprite, definition.visible, definition.persistent, definition.textureMaskId);

            newGM.image_xscale = obj.ScaleX;
            newGM.image_yscale = obj.ScaleY;
            newGM.image_blend = obj.Color;
            newGM.image_alpha = obj.Color.ABGRToCol4().A;
            newGM.image_angle = obj.Rotation;
            newGM.image_index = obj.FrameIndex;
            newGM.image_speed = obj.ImageSpeed;

            createdObjects.Add((newGM, obj));
        }

        foreach (var layer in CurrentRoom.RoomAsset.Layers)
        {
            DebugLog.LogInfo($"Creating layer {layer.LayerName}...");

            var layerContainer = new LayerContainer(layer);

            foreach (var element in layer.Elements)
            {
                element.Layer = layerContainer;

                if (element.Type == ElementType.Instance)
                {
                    var item = (element as GameObject)!;

                    // FirstOrDefault, not First: an instance skipped above (a persistent one already
                    // alive with this id) has nothing to put on the layer, and it already has its
                    // depth from the last time it was created.
                    var obj = createdObjects.FirstOrDefault(x => x.go.InstanceID == item.InstanceID);
                    if (obj.gm == null)
                    {
                        continue;
                    }

                    obj.gm.depth = layer.LayerDepth;
                    layerContainer.ElementsToDraw.Add(obj.gm);
                }
                else if (element.Type == ElementType.Tilemap)
                {
                    var item = (element as CLayerTilemapElement)!;

                    if (item.BackgroundIndex == -1)
                    {
                        continue;
                    }

                    var tilesLayer = new GMTilesLayer(item)
                    {
                        depth = layer.LayerDepth,
                        instanceId = item.Id
                    };

                    layerContainer.ElementsToDraw.Add(tilesLayer);
                }
                else if (element.Type == ElementType.Background)
                {
                    var item = (element as CLayerBackgroundElement)!;

                    var background = new GMBackground(item)
                    {
                        depth = layer.LayerDepth
                    };

                    layerContainer.ElementsToDraw.Add(background);
                }
                else if (element.Type == ElementType.Tile)
                {
                    var tile = (element as CLayerTileElement)!;

                    var newTile = new GMTile()
                    {
                        X = tile.X,
                        Y = tile.Y,
                        Definition = tile.Definition,
                        SpriteMode = tile.SpriteMode,
                        left = tile.SourceLeft,
                        top = tile.SourceTop,
                        width = tile.SourceWidth,
                        height = tile.SourceHeight,
                        depth = layer.LayerDepth,
                        instanceId = tile.Id, // todo : this is almost definitely very wrong
                        Visible = layer.IsVisible,
                        XScale = tile.ScaleX,
                        YScale = tile.ScaleY,
                        Color = tile.Color
                    };

                    layerContainer.ElementsToDraw.Add(newTile);
                }
                else if (element.Type == ElementType.Sprite)
                {
                    var sprite = (element as CLayerSpriteElement)!;
                    var newSprite = new GMSprite(sprite, layer.LayerDepth);
                    layerContainer.ElementsToDraw.Add(newSprite);
                }
                else if (element.Type == ElementType.Sequence)
                {
                    var sequence = (element as CLayerSequenceElement)!;

                    var newSequence = new GMSequence(sequence);
                    newSequence.ImageSpeed = sequence.ImageSpeed;
                    newSequence.SpeedType = sequence.SpeedType;
                    newSequence.ImageScaleX = sequence.ImageScaleX;
                    newSequence.ImageScaleY = sequence.ImageScaleY;
                    newSequence.ImageAngle = sequence.ImageAngle;
                    newSequence.ImageBlend = sequence.ImageBlend;
                    newSequence.ImageAlpha = sequence.ImageAlpha;
                    newSequence.X = sequence.X;
                    newSequence.Y = sequence.Y;
                    //newSequence.HeadPosition = sequence.HeadPosition;

                    layerContainer.ElementsToDraw.Add(newSequence);
                }
                else
                {
                    DebugLog.LogError($"Don't know how to load element type {element.Type}!");
                }
            }

            CurrentRoom.Layers.Add(layerContainer.ID, layerContainer);
        }

        // Must run before any object's Create event (below) -- games can read background_*[i] as
        // early as their very first Create event (e.g. jtool's own oWorld_Create_0), and this used
        // to run after object creation entirely, so CurrentRoom.OldBackgrounds was still completely
        // empty (padding included) the first time any such read happened, crashing regardless of
        // the padding fix below.
        foreach (var item in CurrentRoom.RoomAsset.OldBackgrounds)
        {
            var oldBackground = new GMOldBackground()
            {
                Enabled = item.Enabled,
                Foreground = item.Foreground,
                Definition = item.Definition,
                Position = item.Position,
                TilingX = item.TilingX,
                TilingY = item.TilingY,
                Speed = item.Speed,
                Stretch = item.Stretch
            };

            CurrentRoom.OldBackgrounds.Add(oldBackground);
        }

        // Real GM8 rooms always have exactly 8 legacy background slots internally (background_*[0..7]
        // are safely readable/writable regardless of how many a room's editor actually configured --
        // unused slots just read back as disabled/empty), not just however many the room asset
        // happened to define. Padding here avoids an IndexOutOfRangeException the first time GML
        // code touches a background_*[i] slot the room editor never explicitly set up.
        while (CurrentRoom.OldBackgrounds.Count < 8)
        {
            CurrentRoom.OldBackgrounds.Add(new GMOldBackground
            {
                Enabled = false,
                Foreground = false,
                Definition = -1,
                Position = default,
                TilingX = false,
                TilingY = false,
                Speed = default,
                Stretch = false
            });
        }

        // instance_exists will still return true for all objects even in Create of the first object.... ugh
        DebugLog.LogInfo($"Calling PreCreate/Create/CC...");
        foreach (var item in createdObjects)
        {
            RunObjEvents(item.gm, item.go);
        }

        DebugLog.LogInfo($"Creating loose tiles...");
        foreach (var item in CurrentRoom.RoomAsset.Tiles)
        {
            var newTile = new GMTile()
            {
                X = item.X,
                Y = item.Y,
                left = item.SourceLeft,
                top = item.SourceTop,
                width = item.SourceWidth,
                height = item.SourceHeight,
                depth = item.Depth,
                instanceId = item.InstanceID,
                XScale = item.ScaleX,
                YScale = item.ScaleY,
                Color = item.Color,
                Definition = item.Definition,
                SpriteMode = item.SpriteMode,
                Visible = true
            };

            CurrentRoom.Tiles.Add(newTile);
        }

        var currentInstances = InstanceManager.instances.Values.ToList();

        if (FirstRoom)
        {
            FirstRoom = false;
            DebugLog.LogInfo($"Calling GameStart...");
            foreach (var obj in currentInstances)
            {
                GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.Other, (int)EventSubtypeOther.GameStart);
            }
        }

        DebugLog.LogInfo($"Calling RoomCC...");
        // room creation code is called with a dummy object that gets de-referenced immediately after
        var createCode = GetCodeFromCodeIndex(CurrentRoom.RoomAsset.CreationCodeId);
        if (createCode != null)
        {
            var dummy = new GMLObject();
            VMExecutor.ExecuteCode(createCode, dummy);
            dummy = null;

            if (VMExecutor.EnvStack.Count != 0)
            {
                DebugLog.LogWarning("EnvStack not empty after room creation code!");
                foreach (var item in VMExecutor.EnvStack)
                {
                    if (item == null)
                    {
                        DebugLog.LogWarning(" - NULL");
                    }
                    else
                    {
                        if (item.Self is GamemakerObject)
                        {
                            DebugLog.LogWarning($" - {item.ObjectDefinition?.Name} ({item.GMSelf.instanceId})");
                        }
                        else
                        {
                            DebugLog.LogWarning($" - {item.GetType()}");
                        }
                    }
                }

                // HACK: why does the dummy object stay on envstack? no idea!!!
                VMExecutor.EnvStack.Clear();
            }
        }

        DebugLog.LogInfo($"Calling RoomStart...");
        foreach (var obj in currentInstances)
        {
            GamemakerObject.ExecuteEvent(obj, obj.Definition, EventType.Other, (int)EventSubtypeOther.RoomStart);
        }

        VMCode? GetCodeFromCodeIndex(int codeIndex)
        {
            if (codeIndex == -1)
            {
                return null;
            }

            return GameLoader.Codes[codeIndex];
        }

        GC.Collect(); // gc on load boundary

        DebugLog.LogInfo($"- Finished room change.");

        RoomLoaded = true;
    }

    public static void room_goto_next()
    {
        var order = GameLoader.GeneralInfo.RoomOrder;
        var idx = Array.IndexOf(order, CurrentRoom.AssetId);

        if (order.Length > idx + 1)
        {
            New_Room = order[idx + 1];
        }

    }

    public static void room_goto_previous()
    {
        var order = GameLoader.GeneralInfo.RoomOrder;
        var idx = Array.IndexOf(order, CurrentRoom.AssetId);

        if (idx > 0)
        {
            New_Room = order[idx - 1];
        }
    }

    public static int room_next(int numb)
    {
        var order = GameLoader.GeneralInfo.RoomOrder;
        var idx = Array.IndexOf(order, numb);

        if (order.Length > idx + 1)
        {
            return order[idx + 1];
        }

        return -1;
    }

    public static int room_previous(int numb)
    {
        var order = GameLoader.GeneralInfo.RoomOrder;
        var idx = Array.IndexOf(order, numb);

        if (idx <= 0)
        {
            return -1;
        }

        return order[idx - 1];
    }
}
