using OpenGM.IO;
using OpenGM.Particles;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using UndertaleModLib.Models;
using EventType = OpenGM.VirtualMachine.EventType;

namespace OpenGM.Rendering;

public static class DrawManager
{
    public static int CirclePrecision = 24;

    public static List<DrawWithDepth> _drawObjects = new();

    /// <summary>
    /// this sets view area during gui events. im told this is not how gamemaker does it, but it seems to work
    /// </summary>
    public static Vector2i? GuiSize = null;

    public static bool DebugBBoxes = false;
    public static bool ShouldDrawGui = true;

    public static EventType EventType;
    public static int EventNumber;

    public static int ViewCurrent;
    public static RuntimeView? CurrentRenderingView;

    /// <summary>
    /// The order a frame's <see cref="DrawWithDepth"/>s are drawn in: back to front, i.e. highest
    /// depth first.
    ///
    /// Real GameMaker never puts instances and (pre-GMS2, room-editor/<c>tile_add</c>) tiles in one
    /// list to sort: it keeps a depth-sorted list per kind and merges them at draw time, taking a
    /// tile only while its depth is strictly greater than the next instance's. So a depth tie
    /// between the two kinds is broken by *kind*, not by id -- every instance at depth d is drawn
    /// before, i.e. behind, every tile at depth d. (Same rule in OpenGMK's GM8 runner, which walks
    /// the same merge: <c>gm8emulator/src/game/draw.rs</c>, "if inst_depth &lt; tile_depth".)
    ///
    /// Sorting both kinds together by (depth, instanceId) got this backwards, because the two ids
    /// come from completely unrelated counters: GEN8's LastTile puts tile ids around 10,000,000
    /// while instance ids start just past LastObject (~100,000), so a tile always compared as the
    /// higher id and always landed on the far side of the tie. Continuity's co-op stage relies on
    /// the real order -- its block tiles sit at depth 100 exactly on top of the depth-100
    /// objSpikeUp/objSpikeDown instances they're meant to hide, so drawing tiles first left the
    /// spikes plainly visible on top of the blocks.
    /// </summary>
    private sealed class DrawOrderComparer : IComparer<DrawWithDepth>
    {
        public static readonly DrawOrderComparer Instance = new();

        public int Compare(DrawWithDepth? a, DrawWithDepth? b)
        {
            // higher depth is further back, so it's drawn first
            var byDepth = b!.depth.CompareTo(a!.depth);
            if (byDepth != 0)
            {
                return byDepth;
            }

            var byKind = IsTile(a).CompareTo(IsTile(b));
            if (byKind != 0)
            {
                return byKind;
            }

            if (IsTile(a))
            {
                // tiles are drawn in the order they were added within a depth, which for a room's
                // own tiles is the room editor's order -- ascending id.
                return a.instanceId.CompareTo(b.instanceId);
            }

            return CompatFlags.DepthSortingReverseInstanceIds
                ? b.instanceId.CompareTo(a.instanceId)
                : a.instanceId.CompareTo(b.instanceId);
        }

        // Only legacy tiles are their own kind here. GMS2's layer elements (GMTilesLayer,
        // GMBackground, GMSprite) are ordered by their layer's depth like the instances on that
        // layer, so they stay in the instance group.
        private static bool IsTile(DrawWithDepth item) => item is GMTile;
    }

    public static void Register(DrawWithDepth obj)
    {
        if (_drawObjects.Contains(obj))
        {
            return;
        }

        _drawObjects.Add(obj);
    }

    public static void Unregister(DrawWithDepth obj)
    {
        if (!_drawObjects.Contains(obj))
        {
            return;
        }

        _drawObjects.Remove(obj);
    }

    /// <summary>
    /// The instances one per-instance event pass (Begin Step, timelines, Alarm, Step, End Step)
    /// dispatches to: grouped by object index, and in creation order within each object.
    ///
    /// Real GameMaker dispatches these per OBJECT, not per instance. <c>event_holders[ev]</c> is a
    /// list of object indices -- built by walking the object table in index order, so ascending --
    /// and every instance of one object runs before any instance of the next (OpenGMK's
    /// <c>run_object_event</c>). Ordering by instance id instead isn't just a different order,
    /// it's a systematically biased one: every room-placed instance has a lower id than anything
    /// created at runtime, so a runtime-created instance always ran last. The player is always
    /// runtime-created (objPlayerStart calls instance_create), which put objPlayer -- object index
    /// 12, i.e. near-first for real GameMaker -- dead last instead.
    ///
    /// Continuity's ceiling stage depends on the real order. objCeilingExtraJump (object 158) sets
    /// <c>objPlayer.vspeed = -30</c> when touched, and objPlayer (object 12) clamps its own vspeed
    /// to maxVspeed (9) in its own Step. Running the player first leaves the -30 standing for one
    /// frame of movement -- worth 29.6px of extra height, since gravity only takes 0.4 off it
    /// before the move -- and clamps it on the next step instead. Running the player last clamped
    /// the -30 to -9 before it had moved anything, costing almost a whole 32px block of jump
    /// height (a 126px jump became 97px) and making the stage's longer jumps impossible.
    ///
    /// One deliberate simplification: real GameMaker appends an object's descendants to the list
    /// right after the object itself, so a subclass can precede an unrelated lower-indexed object.
    /// OpenGMK holds those descendants in a <c>HashSet</c>, so that relative order isn't even
    /// well-defined as ground truth; plain ascending object index is deterministic and agrees with
    /// it wherever it is.
    ///
    /// == and the snapshot is per PASS, not per frame ==
    ///
    /// Each pass dispatches to the instances that existed when THAT PASS began. An instance created
    /// part-way through a pass never takes part in that pass -- but it takes part in every later
    /// pass of the same frame, and it is drawn that frame.
    ///
    /// Measured on GameMaker Studio 1.4.9999 (a probe project, objSpwn/objSpwnLo/objSpwnHi). objSpwn
    /// (object 11) creates one instance of object 10 and one of object 12 from its Step. Neither runs
    /// a Step that frame -- not even object 12, whose turn in an ascending-object-index walk would
    /// still have been ahead of the creator's -- and both run an End Step that same frame, ordered
    /// 10, 11, 12 with the creator. From the next frame on both behave normally.
    ///
    /// So this is NOT the live per-object walk OpenGMK does (<c>run_object_event</c> building a fresh
    /// <c>iter_by_object</c> as it goes), which would have given object 12 that Step. It is also not
    /// a single snapshot taken at the top of the frame, which is what this used to be: that excluded
    /// an instance created mid-frame from the whole rest of the frame's logic while still drawing it,
    /// so it got exactly one frame on screen with nothing but its Create event applied.
    ///
    /// I Wanna Spook Jam's headless-horseman phase is what that looked like. Every pumpkin and cherry
    /// there creates a companion objPolarBossHHSpotlight, whose only code is an End Step that copies
    /// its parent's position and alpha; a fresh instance's image_alpha is 1, and the pumpkins spend
    /// their first second at alpha 0 waiting to fade in. With the End Step skipped on the creation
    /// frame, the eight spotlights of the first pumpkin ring -- created together at the centre of the
    /// room at triple scale -- drew at full opacity for exactly one frame: a white blob flashing in
    /// the middle of the screen a second before the ring appears.
    ///
    /// Deactivated/destroyed instances are dropped here, and callers re-check at dispatch time: an
    /// instance can be destroyed by an earlier instance's event in the same pass, after this list was
    /// taken. GamemakerObject.ExecuteEvent refuses those anyway, but UpdateAlarms and UpdateTimeline
    /// advance their own counters before reaching it.
    /// </summary>
    public static List<GamemakerObject> EventDispatchOrder()
    {
        var result = new List<GamemakerObject>();

        foreach (var objectIndex in InstanceManager.SortedObjectIndices)
        {
            foreach (var instance in InstanceManager.ObjectMap[objectIndex].Instances)
            {
                if (CanRunEvent(instance))
                {
                    result.Add(instance);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether an instance is still eligible to run a per-instance event. Re-checked at dispatch
    /// time as well as when <see cref="EventDispatchOrder"/> takes its snapshot, because anything
    /// earlier in the same pass can have destroyed or deactivated it since.
    /// </summary>
    private static bool CanRunEvent(GamemakerObject gm)
        => gm.Active && !gm.Marked && !gm.Destroyed;

    private static bool RunStepScript(EventSubtypeStep stepType)
    {
        foreach (var gm in EventDispatchOrder())
        {
            if (CanRunEvent(gm) && gm._createRan && RoomManager.RoomLoaded)
            {
                GamemakerObject.ExecuteEvent(gm, gm.Definition, EventType.Step, (int)stepType);
            }
        }

        if (RoomManager.New_Room != -1)
        {
            RoomManager.ChangeToWaitingRoom();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an instance takes part in this frame's draw at all.
    ///
    /// Marked/Destroyed are in here because <see cref="FixedUpdate"/> snapshots the draw list
    /// *before* DoAStep runs -- an instance that destroyed itself during the step is still in that
    /// snapshot, and RemoveMarked only unlinks it for the next frame. GameMaker drops an instance
    /// the moment instance_destroy runs, so it must not get one more frame on screen.
    ///
    /// That was invisible until sprite animation started wrapping immediately. The stray draw showed
    /// whatever image_index happened to hold, and while a finished animation rested at image_number
    /// a (since-removed) special case rendered that as the last frame, so it looked continuous. With
    /// the wrap applied straight away it became frame 0 -- I Wanna Spook Jam's jump smoke is an objFx
    /// that destroys itself in its animation-end event, and it flashed the big fresh puff for a frame
    /// just as the smoke finished dissipating.
    /// </summary>
    public static bool ShouldDraw(GamemakerObject gm)
        => gm._createRan
           && RoomManager.RoomLoaded
           && gm.visible
           && gm.Active
           && !gm.Marked
           && !gm.Destroyed;

    private static bool RunDrawScript(IReadOnlyList<DrawWithDepth> items, EventSubtypeDraw drawType)
    {
        EventType = EventType.Draw;
        EventNumber = (int)drawType;

        foreach (var (id, layer) in RoomManager.CurrentRoom.Layers)
        {
            if (layer.BeginScript != null)
            {
                VMExecutor.ExecuteCode(layer.BeginScript.GetCode(), null);
            }
        }

        foreach (var item in items)
        {
            if (GraphicsManager.ForceDepth)
            {
                GraphicsManager.GR_Depth = CustomMath.Min(16000, CustomMath.Max(-16000, GraphicsManager.ForcedDepth));
            }
            else
            {
                GraphicsManager.GR_Depth = CustomMath.Min(16000, CustomMath.Max(-16000, item.depth));
            }

            if (item is GamemakerObject gm)
            {
                if (!ShouldDraw(gm))
                {
                    continue;
                }

                if (drawType == EventSubtypeDraw.Draw)
                {
                    var hasDrawScript = gm.Definition.DrawScript.ContainsKey(EventSubtypeDraw.Draw);

                    var currentParent = gm.Definition.parent;
                    while (currentParent != null)
                    {
                        if (currentParent.DrawScript.ContainsKey(EventSubtypeDraw.Draw))
                        {
                            hasDrawScript = true;
                            break;
                        }

                        currentParent = currentParent.parent;
                    }

                    if (!hasDrawScript)
                    {
                        GraphicsManager.PushMessage($"{drawType} (DRAW SELF) {gm.instanceId} ({AssetIndexManager.GetName(AssetType.objects, gm.object_index)})");
                        SpriteManager.DrawSelf(gm);
                        GraphicsManager.PopMessage();
                        continue;
                    }
                }

                GraphicsManager.PushMessage($"{drawType} {gm.instanceId} ({AssetIndexManager.GetName(AssetType.objects, gm.object_index)})");
                GamemakerObject.ExecuteEvent(gm, gm.Definition, EventType.Draw, (int)drawType);
                GraphicsManager.PopMessage();
            }
            else if (drawType == EventSubtypeDraw.Draw)
            {
                GraphicsManager.PushMessage($"{drawType} {item.instanceId} ({item.GetType().Name})");
                item.Draw();
                GraphicsManager.PopMessage();
            }
        }

        foreach (var (id, layer) in RoomManager.CurrentRoom.Layers)
        {
            if (layer.EndScript != null)
            {
                VMExecutor.ExecuteCode(layer.EndScript.GetCode(), null);
            }
        }

        if (RoomManager.New_Room != -1)
        {
            RoomManager.ChangeToWaitingRoom();
            return true;
        }

        return false;
    }

    public static bool DoAStep(List<DrawWithDepth> objs)
    {
        // g_pBuiltIn.delta_time = (g_CurrentTime - g_pBuiltIn.last_time)*1000;
        // g_pBuiltIn.last_time = g_CurrentTime;
        // ResetSpriteMessageEvents();
        // g_pIOManager.StartStep();
        // HandleOSEvents();
        // g_pGamepadManager.Update();

        InstanceManager.RememberOldPositions(); // also advances sprite animations
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        // g_pLayerManager.UpdateLayers();
        // g_pSequenceManager.PerformInstanceEvents(g_RunRoom, EVENT_STEP_BEGIN);

        // Only the collision pass takes a list; every per-instance event pass below reads the live
        // instance lists instead, via EventDispatchOrder -- see there for why that has to be live.
        var stepList = objs.Where(x => x is not GamemakerObject obj || obj.Active).OrderBy(x => x.instanceId).ToList();
        var beginStepChangedRoom = RunStepScript(EventSubtypeStep.BeginStep);
        if (beginStepChangedRoom)
        {
            return true;
        }

        // resize event

        AsyncManager.HandleAsyncQueue();
        NetworkFunctions.HandleReceived();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        HandleTimeLine();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        // HandleTimeSources();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        HandleAlarm();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        HandleKeyboard();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        HandleMouse();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        // g_pEffectsManager.StepEffectsForRoom(g_RunRoom);

        // g_pSequenceManager.UpdateInstancesForRoom(g_RunRoom);
        // g_pSequenceManager.PerformInstanceEvents(g_RunRoom, EVENT_STEP_NORMAL);

        var stepChangedRoom = RunStepScript(EventSubtypeStep.Step);
        if (stepChangedRoom)
        {
            return true;
        }

        // ProcessSpriteMessageEvents();
        InstanceManager.UpdatePositions(); // UpdateInstancePositions

        HandleOther();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        // YYPushEventsDispatch();
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        UpdateCollisions(stepList);
        if (RoomManager.CheckAndChangeRoom())
        {
            return true;
        }

        // g_pSequenceManager.PerformInstanceEvents(g_RunRoom, EVENT_STEP_END);

        var endStepChangedRoom = RunStepScript(EventSubtypeStep.EndStep);
        if (endStepChangedRoom)
        {
            return true;
        }

        ParticleManager.UpdateAllSystems();

        RoomManager.CurrentRoom?.RemoveMarked();

        return false;
    }

    /// <summary>
    /// Runs between the Begin Step and Alarm events, which is where GameMaker (and OpenGMK's
    /// Game::frame) advances timelines.
    /// </summary>
    public static void HandleTimeLine()
    {
        foreach (var gm in EventDispatchOrder())
        {
            if (CanRunEvent(gm) && gm._createRan && RoomManager.RoomLoaded)
            {
                gm.UpdateTimeline();
            }
        }
    }

    public static void HandleAlarm()
    {
        foreach (var gm in EventDispatchOrder())
        {
            if (CanRunEvent(gm))
            {
                gm.UpdateAlarms();
            }
        }
    }

    /// <summary>
    /// Undoes this frame's automatic hspeed/vspeed motion (already applied by
    /// <see cref="InstanceManager.UpdatePositions"/> before this runs), snapping the instance back
    /// to wherever it was at the start of this frame.
    /// </summary>
    private static void RevertToPreviousPosition(GamemakerObject gm)
    {
        gm.x = gm.xprevious;
        gm.y = gm.yprevious;
    }

    /// <summary>Re-applies hspeed/vspeed to x/y, matching InstanceManager.UpdatePositions's own logic.</summary>
    private static void ApplySpeed(GamemakerObject gm)
    {
        if (gm.hspeed != 0 || gm.vspeed != 0)
        {
            gm.x += gm.hspeed;
            gm.y += gm.vspeed;
        }
    }

    /// <summary>
    /// One spatial grid per target object, built lazily and topped up incrementally across a
    /// whole UpdateCollisions call -- see SpatialGrid.cs. ConstituentIds is every object id whose
    /// own instance list feeds this target (the target asset id itself plus every descendant,
    /// mirroring InstanceManager.FindByAssetId's own recursive walk), and Cursors tracks how far
    /// into each of those lists this grid has already consumed. Safe to cache for the whole call:
    /// InstanceManager.ObjectMap[id].Instances is append-only for the duration of a single
    /// UpdateCollisions (instance_destroy only marks for later removal; the list itself isn't
    /// shrunk until RemoveMarked(), which runs after this function returns) -- so topping up a
    /// cursor never has to account for entries shifting out from under it.
    /// </summary>
    private class CollisionGridEntry
    {
        public readonly SpatialGrid Grid = new();
        public List<int> ConstituentIds = null!;
        public readonly Dictionary<int, int> Cursors = new();
    }

    /// <summary>
    /// Gets (creating and/or topping up as needed) the target object's cached grid, and returns
    /// every candidate near <paramref name="driver"/>'s current bbox -- a superset of what's
    /// actually colliding, still subject to the same CollisionManager.IsValidCollisionCandidate
    /// precise test the caller already applies. Pure broad-phase pruning: never changes which
    /// pairs end up colliding, only how many pairs need the precise test run on them.
    /// </summary>
    private static List<GamemakerObject> GetCollisionCandidates(Dictionary<int, CollisionGridEntry> grids, int targetAssetId, GamemakerObject driver)
    {
        if (!grids.TryGetValue(targetAssetId, out var entry))
        {
            entry = new CollisionGridEntry { ConstituentIds = CollectDescendantIds(targetAssetId) };
            grids[targetAssetId] = entry;
        }

        foreach (var id in entry.ConstituentIds)
        {
            var instances = InstanceManager.ObjectMap[id].Instances;
            entry.Cursors.TryGetValue(id, out var cursor);

            for (var i = cursor; i < instances.Count; i++)
            {
                var instance = instances[i];
                entry.Grid.Insert(instance, instance.bbox); // getter recomputes on staleness
            }

            entry.Cursors[id] = instances.Count;
        }

        return entry.Grid.Query(driver.bbox);
    }

    /// <summary>Every object id FindByAssetId(assetId) would pull instances from: assetId itself plus every descendant.</summary>
    private static List<int> CollectDescendantIds(int assetId)
    {
        var ids = new List<int>();

        void Walk(int id)
        {
            ids.Add(id);
            foreach (var child in InstanceManager.ObjectMap[id].ChildrenIndexes)
            {
                Walk(child);
            }
        }

        Walk(assetId);
        return ids;
    }

    /// <summary>
    /// Whether an instance can still take part in a collision pair -- OpenGMK's
    /// <c>Instance::is_active()</c>, which <c>run_collisions</c> tests on both sides of every pair.
    ///
    /// The subtle half is <see cref="GamemakerObject.Marked"/>. GML's <c>instance_destroy()</c>
    /// only MARKS an instance (InstanceManager.MarkForDestruction); <see cref="GamemakerObject.Destroyed"/>
    /// isn't set until RemoveMarked() actually deletes it, which happens long after
    /// <see cref="UpdateCollisions"/> has returned. So a liveness check written against
    /// <c>Destroyed</c> alone is never true for the case that actually matters -- an instance
    /// destroyed by this very frame's Collision code -- and that instance carries on colliding with
    /// every remaining candidate as if nothing had happened.
    ///
    /// Concretely: Spook Jam's tutorial room has two objIntroTinyMushroom patrolling the same strip
    /// of floor, walking through each other. objBullet is object 12 and the mushroom 112, so the
    /// bullet drives the pair and finds both mushrooms in the same frame; each mushroom's
    /// Collision(objBullet) destroys itself AND the bullet (<c>with (other) instance_destroy()</c>).
    /// Real GameMaker abandons the bullet's whole turn right there, so the second mushroom survives
    /// -- here the already-dead bullet went on to kill it too, and one shot took out both.
    /// </summary>
    private static bool IsLiveCollisionParticipant(GamemakerObject instance)
        => !instance.Destroyed && !instance.Marked && instance.Active;

    /// <summary>
    /// Re-buckets an instance in every currently-built grid it's already a member of, after
    /// something has repositioned it (the solid revert-to-previous-position logic below) -- a
    /// no-op for any grid it was never inserted into in the first place.
    /// </summary>
    private static void RegridIfPresent(Dictionary<int, CollisionGridEntry> grids, GamemakerObject instance)
    {
        if (grids.Count == 0)
        {
            return;
        }

        foreach (var entry in grids.Values)
        {
            if (entry.Grid.Contains(instance))
            {
                entry.Grid.Reinsert(instance, instance.bbox); // getter recomputes on staleness
            }
        }
    }

    public static void UpdateCollisions(IReadOnlyList<DrawWithDepth> stepList)
    {
        var mergedTargets = GetMergedCollisionTargets();

        if (mergedTargets.Count == 0)
        {
            return;
        }

        // See GetCollisionCandidates/RegridIfPresent above -- one entry per target object,
        // cached for this whole call.
        var grids = new Dictionary<int, CollisionGridEntry>();

        // A Collision event registered against an object applies to every instance that IS-A that
        // object, its descendants included -- so the DRIVING side of a merged pair has to gather
        // descendants too, not just exact-type matches (the target side already does, via
        // Command_InstancePlace/FindByAssetId). Keying drivers by exact definition silently
        // dropped whole pairs in any room whose instances are all a subclass: Continuity's NANG
        // rooms place only Block9 (a child of objBlock) and no objBlock at all, so the merged
        // (objBlock, playerTeleport) pair drove off an empty instance list and the teleport clone
        // flew straight through every wall. The hub places real objBlock instances alongside its
        // Block9-sprited ones, so the same pair was driven there -- which is what made the bug
        // look room-specific rather than structural.
        // Preserves stepList's own ascending-instance-id order within each driver.
        var driverInstances = new Dictionary<ObjectDefinition, List<GamemakerObject>>();
        foreach (var gmo in stepList.OfType<GamemakerObject>())
        {
            for (var def = gmo.Definition; def != null; def = def.parent)
            {
                if (!mergedTargets.ContainsKey(def))
                {
                    continue;
                }

                if (!driverInstances.TryGetValue(def, out var list))
                {
                    list = new List<GamemakerObject>();
                    driverInstances[def] = list;
                }

                list.Add(gmo);
            }
        }

        // Real GameMaker fires exactly one Collision event per colliding instance pair per step.
        // Dedups instances already handled via a MORE SPECIFIC target (see the "most specific
        // target" resolution below), and -- now that one instance can drive under several
        // definitions at once (both objBlock and Block9 are merge drivers as soon as anything
        // authors Collision events against both) -- stops the same pair firing once per driver
        // definition as well.
        var handledPairs = new HashSet<(int Driver, int Collide)>();

        // Real GameMaker drives every collision pair from the LOWER-indexed object's own
        // per-instance turn, regardless of which side actually authored the Collision_N GML (see
        // GetMergedCollisionTargets) -- so the outer loop here has to walk driver object
        // definitions in ascending asset-index order too, not just instances in whatever order
        // they happen to appear in stepList.
        foreach (var driverDef in mergedTargets.Keys.OrderBy(d => d.AssetId))
        {
            var targets = mergedTargets[driverDef];

            if (!driverInstances.TryGetValue(driverDef, out var drivers))
            {
                continue;
            }

            foreach (var gmo in drivers)
            {
                // Real GameMaker processes an instance's registered collision target types in
                // ascending target-object-index order every frame (fill_event_holders' explicit
                // `list.sort()`), completely independent of which Collision_N sub-event was authored
                // first in the object editor, or which side of the pair authored it at all -- for
                // an instance with multiple simultaneously-true collisions this frame (e.g. landing
                // exactly flush against both a solid block and a non-solid spike), checking them in
                // the wrong relative order changes which one's solid-revert side effects run first,
                // which can then feed a stale/incorrect position into the other's check.
                foreach (var targetAssetId in targets)
                {
                    if (!IsLiveCollisionParticipant(gmo))
                    {
                        break;
                    }

                    // Command_InstancePlace's plain return value is only ITS OWN last match, not
                    // every instance overlapping this frame -- when a single targetAssetId (e.g. the
                    // broad objBlock) simultaneously matches more than one actually-different
                    // instance (a non-solid objPlatform instance the driver is already resting on,
                    // AND a genuinely solid objBlock wall instance beside it -- both are objBlock
                    // descendants), only asking for the single return would silently drop whichever
                    // one isn't "last": if that happens to be the wall, and the platform was already
                    // handledThisTurn (via its own, more specific target entry), the wall's collision
                    // is skipped ENTIRELY this frame -- exactly the gap that let the driver overshoot
                    // an extra frame's worth of hspeed into the wall before its Collision code ever
                    // got a chance to run, past the point move_contact_solid's "already colliding, do
                    // nothing" rule could still recover it. Walking the broad-phase candidates gives
                    // every match its own turn below.
                    foreach (var collide in GetCollisionCandidates(grids, targetAssetId, gmo))
                    {
                        // Stops as soon as the driver itself is gone -- including when it was
                        // destroyed by the Collision code of a pair handled moments ago, in this
                        // same loop. Real GameMaker abandons an instance's whole turn at that point
                        // (OpenGMK's `if !inst1.is_active() { continue 'iter1 }`); a dead instance
                        // can't go on colliding with the rest of the frame's candidates.
                        if (!IsLiveCollisionParticipant(gmo))
                        {
                            break;
                        }

                        // Each pair is tested for collision HERE, as it's handled -- not once up
                        // front for the whole candidate list. Resolving one pair moves the driver,
                        // and that's usually exactly what ends its collision with the rest of the
                        // list; real GameMaker only ever fires a Collision event for a pair that
                        // still collides at that moment (OpenGMK's run_collisions runs
                        // check_collision inside its own candidate loop). This also covers a collide
                        // that an earlier pair's event destroyed or deactivated.
                        //
                        // Concretely: a floor is a row of separate 32x32 objBlock instances and
                        // objPlayer's mask is only 11px wide, so a landing straddles two of them
                        // about a third of the time and finds both. Resolving the first lands the
                        // player (move_contact_solid, then vspeed = 0); handling the second
                        // unconditionally then snapped it back to yprevious for a collision that no
                        // longer existed, leaving it hanging where it started the frame with no
                        // speed left. Gravity had to build back up from 0, so the last few pixels of
                        // every such fall crawled by over several frames instead of landing on the
                        // spot (Spook Jam -- and the same for running into a wall of stacked blocks,
                        // which stopped the driver short by an extra frame's worth of hspeed).
                        if (!CollisionManager.IsValidCollisionCandidate(gmo, collide))
                        {
                            continue;
                        }

                        // One Collision event per colliding PAIR, however many of this driver's
                        // targets happen to match the same instance. GetCollisionCandidates matches
                        // a target's descendants too, so an instance whose real type is a descendant
                        // of one target and is itself another target (e.g. an objPlatform found
                        // under both Collision_objPlatform and its ancestor Collision_objBlock)
                        // otherwise gets a turn per matching target instead of one turn total.
                        // Which handler that single turn fires is decided below, per side.
                        if (!handledPairs.Add((gmo.instanceId, collide.instanceId)))
                        {
                            continue;
                        }
                        // Real GameMaker's own engine (not user GML) automatically snaps BOTH
                        // instances back to their pre-move (xprevious/yprevious) position before
                        // running the Collision event whenever either side is solid -- by the point
                        // any user code (move_contact_solid, place_free, etc.) runs, the instance is
                        // never already-penetrating, so those checks can actually find the real
                        // boundary instead of only ever detecting "still blocked" from inside the
                        // solid and being unable to correct it (this was silently missing before,
                        // and is why solid collision resolution looked "wrong"/slow instead of
                        // stopping cleanly at walls).
                        var eitherSolid = gmo.solid || collide.solid;

                        if (eitherSolid)
                        {
                            RevertToPreviousPosition(gmo);
                            RevertToPreviousPosition(collide);
                            RegridIfPresent(grids, gmo);
                            RegridIfPresent(grids, collide);
                        }

                        // Real GameMaker fires BOTH sides' Collision code for a colliding pair --
                        // which side's object index actually drove the check above is purely an
                        // implementation detail (OpenGMK's run_collisions always issues both
                        // run_instance_event calls; each is a safe, silent no-op if that side has
                        // no matching Collision_<other> sub-event). Without this, a pair like
                        // "oPlayer collides with oItem" where only oItem authors the GML (targeting
                        // oPlayer) could only ever be discovered from oItem's own turn -- decoupling
                        // it entirely from oPlayer's own same-step collision resolution against
                        // lower-indexed objects (e.g. landing on solid ground) that real GameMaker
                        // guarantees already happened by this point.
                        // `other` inside the event is resolved by VMExecutor.Other, which walks the
                        // env stack expecting a [self, null-marker, other] layout (the null marker is
                        // what PUSHENV pushes for a `with` block). Push the null marker between the
                        // `other` frame and the self frame ExecuteEvent is about to push, or `other`
                        // walks straight past `collide` and wrongly resolves back to self -- which
                        // silently breaks any collision event that touches `other` (e.g. jtool's
                        // spikes reddening via `with (other) highlight_count = 0`).
                        //
                        // Which of gmo's handlers this fires is decided by walking COLLIDE's own type
                        // chain, most specific first, and running the first one gmo actually has --
                        // exactly what the other side does below with gmo's chain. Real GameMaker
                        // fires one Collision event per colliding pair: the most specific
                        // ancestor-or-self of the other instance's type that THIS instance authored a
                        // handler for. (So a driver with handlers for both objPlatform and its parent
                        // objBlock gets only the objPlatform one when it hits an objPlatform -- the
                        // broader objBlock handler firing too is what used to drag YoYoYo_Engine's
                        // instances into solid geometry, since objBlock's move_contact_solid ran on a
                        // pair that wasn't solid and so had no revert to fall back on.)
                        //
                        // Resolving against `targets` instead of gmo's own handlers is what broke
                        // Spook Jam's Smartkin save blockers: objBullet(12) authors Collision(25) to
                        // delete itself on objSaveBlocker, and oSmartkin_SaveBlocker(920, a child of
                        // 25) authors Collision(12) to fade itself in. Both ids land in the merged
                        // `targets` list -- 920 only because the BLOCKER authored it -- so the walk
                        // picked 920 as "most specific", objBullet had no handler for 920, and its
                        // real Collision(25) never ran. The blocker still faded in from its own side,
                        // so the bullet visibly passed straight through a blocker that reacted to it.
                        VMExecutor.EnvStack.Push(new VMEnvFrame { Self = collide, ObjectDefinition = collide.Definition });
                        VMExecutor.EnvStack.Push(null);
                        for (var t = collide.Definition; t != null; t = t.parent)
                        {
                            if (GamemakerObject.ExecuteEvent(gmo, gmo.Definition, EventType.Collision, t.AssetId))
                            {
                                break;
                            }
                        }
                        VMExecutor.EnvStack.Pop(); // null marker
                        VMExecutor.EnvStack.Pop(); // collide

                        // Deliberately NOT gated on either side still being alive: real GameMaker
                        // issues both of a pair's run_instance_event calls unconditionally, so a
                        // driver whose own Collision code destroyed itself (the "bullet deletes
                        // itself, enemy subtracts hp" pattern) still lets the other side's handler
                        // run for this pair. Only whether the SUBJECT of the event is live gates it,
                        // and GamemakerObject.ExecuteEvent already applies that itself.
                        if (!gmo.Destroyed && !collide.Destroyed)
                        {
                            // collide's own Collision code may target an ancestor of gmo's type
                            // (target-side inheritance) rather than its exact type, so walk upward
                            // the same way ExecuteEvent already does for the subject side. Starts
                            // from gmo's OWN definition, not driverDef: gmo can be a descendant of
                            // the definition that drove this pair, and a handler authored against
                            // that more specific type has to win over the ancestor's.
                            // Same [self, null-marker, other] layout requirement as above so this
                            // side's event sees `other` == gmo rather than resolving back to itself.
                            VMExecutor.EnvStack.Push(new VMEnvFrame { Self = gmo, ObjectDefinition = gmo.Definition });
                            VMExecutor.EnvStack.Push(null);
                            for (var t = gmo.Definition; t != null; t = t.parent)
                            {
                                if (GamemakerObject.ExecuteEvent(collide, collide.Definition, EventType.Collision, t.AssetId))
                                {
                                    break;
                                }
                            }
                            VMExecutor.EnvStack.Pop(); // null marker
                            VMExecutor.EnvStack.Pop(); // gmo
                        }

                        if (eitherSolid)
                        {
                            // The Collision event script (e.g. move_contact_solid, or just setting
                            // hspeed/vspeed to 0) is expected to have adjusted speed to avoid the
                            // solid -- re-apply whatever's left, then as a last-resort safety net (if
                            // the script didn't fully resolve it) revert once more.
                            ApplySpeed(gmo);
                            ApplySpeed(collide);

                            if (IsLiveCollisionParticipant(gmo) && IsLiveCollisionParticipant(collide)
                                && CollisionManager.Collision_Instance(gmo, collide, true))
                            {
                                RevertToPreviousPosition(gmo);
                                RevertToPreviousPosition(collide);
                                RegridIfPresent(grids, gmo);
                                RegridIfPresent(grids, collide);
                            }
                        }
                    }
                }
            }
        }
    }

    private static Dictionary<ObjectDefinition, int[]>? _mergedCollisionTargets;

    /// <summary>
    /// Drops the cached collision-pair and collision-script tables. Both are keyed by
    /// <see cref="ObjectDefinition"/> instances, which only ever change when the asset set is
    /// (re)loaded -- so that's the one point at which they can go stale.
    /// </summary>
    public static void ResetCollisionCaches()
    {
        _mergedCollisionTargets = null;
        _collisionScriptsCache.Clear();
    }

    /// <summary>
    /// Real GameMaker (OpenGMK's fill_event_holders, ground truth for this) only ever processes a
    /// Collision event pair from the LOWER-indexed object's own per-instance turn
    /// (`list.retain(|x| x >= sub)`), regardless of which side of the pair actually authored the
    /// GML -- e.g. if only oItem (a high asset index, added late in development) has a
    /// "Collision with oPlayer" (a low asset index, e.g. 0) event, the check is still driven by
    /// oPlayer's own turn, checking oItem as just another ascending-sorted target alongside
    /// whatever oPlayer's own Collision events target. Without this, oItem's code could only ever
    /// be discovered via oItem's own instance getting its own separate, differently-ordered turn
    /// (previously: sorted by instance creation id) -- decoupling it from guarantees real
    /// GameMaker gives for free, like "a solid landing against a lower-indexed object this same
    /// step has already been resolved before a higher-indexed object's Collision code can read
    /// this instance's position".
    /// </summary>
    private static Dictionary<ObjectDefinition, int[]> GetMergedCollisionTargets()
    {
        if (_mergedCollisionTargets != null)
        {
            return _mergedCollisionTargets;
        }

        var pairs = new HashSet<(int lo, int hi)>();

        foreach (var def in InstanceManager.ObjectDefinitions.Values)
        {
            foreach (var target in GetCollisionScripts(def).Keys)
            {
                var a = def.AssetId;
                pairs.Add(a <= target ? (a, target) : (target, a));
            }
        }

        var byDriver = new Dictionary<ObjectDefinition, List<int>>();

        foreach (var (lo, hi) in pairs)
        {
            if (!InstanceManager.ObjectDefinitions.TryGetValue(lo, out var driverDef))
            {
                continue;
            }

            if (!byDriver.TryGetValue(driverDef, out var list))
            {
                list = new List<int>();
                byDriver[driverDef] = list;
            }

            list.Add(hi);
        }

        _mergedCollisionTargets = byDriver.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().OrderBy(x => x).ToArray());
        return _mergedCollisionTargets;
    }

    // ObjectDefinition's CollisionScript/parent chain is fixed after load, so this is safe to
    // cache for the process lifetime -- was previously rebuilding a fresh Dictionary (walking the
    // whole inheritance chain) for every instance with a Collision event, every single frame.
    private static readonly Dictionary<ObjectDefinition, Dictionary<int, VMCode>> _collisionScriptsCache = new();

    public static Dictionary<int, VMCode> GetCollisionScripts(ObjectDefinition obj)
    {
        if (_collisionScriptsCache.TryGetValue(obj, out var cached))
        {
            return cached;
        }

        var ret = new Dictionary<int, VMCode>();

        void AddScripts(ObjectDefinition obj)
        {
            foreach (var item in obj.CollisionScript)
            {
                if (ret.ContainsKey(item.Key))
                {
                    continue;
                }

                ret.Add(item.Key, item.Value);
            }
        }

        var parent = obj;
        while (parent != null)
        {
            AddScripts(parent);
            parent = parent.parent;
        }

        _collisionScriptsCache[obj] = ret;
        return ret;
    }

    public static void HandleOther()
    {
        // create copy since events can create new instances
        var instances = InstanceManager.instances.Values.ToList();
        foreach (var instance in instances)
        {
            if (!instance.Marked)
            {
                if (instance.HasEvent(EventType.Other, (int)EventSubtypeOther.OutsideRoom))
                {
                    var outside = false;

                    if (SpriteManager.SpriteExists(instance.sprite_index) || SpriteManager.SpriteExists(instance.mask_index))
                    {
                        var bbox = instance.bbox;
                        outside = ((bbox.right < 0) || (bbox.left > RoomManager.CurrentRoom.SizeX) || (bbox.bottom < 0) || (bbox.top > RoomManager.CurrentRoom.SizeY));
                    }
                    else
                    {
                        outside = ((instance.x < 0) || (instance.x > RoomManager.CurrentRoom.SizeX) || (instance.y < 0) || (instance.y > RoomManager.CurrentRoom.SizeY));
                    }

                    if (outside)
                    {
                        if (!instance.IsOutsideRoom)
                        {
                            GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.Other, (int)EventSubtypeOther.OutsideRoom);
                        }
                    }
                    instance.IsOutsideRoom = outside;
                }

                // boundary events

                // outside/boundary view events
            }
        }
    }

    /// <summary>
    /// Additive, browser-wasm-only alternative to <see cref="FixedUpdate"/>: runs the exact same
    /// pre-<see cref="DoAStep"/> bookkeeping and steps game logic for real, but returns before any
    /// of the GL/<see cref="Rendering.CustomWindow.Instance"/>-touching drawing code that follows
    /// in <see cref="FixedUpdate"/> -- the browser build draws elsewhere. Native
    /// behavior (<see cref="FixedUpdate"/> itself) is completely unchanged.
    /// </summary>
    public static void HeadlessStep()
    {
        var itemsToRemove = new List<DrawWithDepth>();
        foreach (var item in _drawObjects)
        {
            if (item is GamemakerObject gm)
            {
                if (gm.Destroyed || gm.Marked)
                {
                    itemsToRemove.Add(item);
                }

                if (!InstanceManager.instance_exists_instanceid(gm.instanceId))
                {
                    gm.Destroyed = true;
                    gm.Marked = true;
                    itemsToRemove.Add(item);
                }
            }
        }
        _drawObjects.RemoveAll(x => itemsToRemove.Contains(x));

        var drawObjectsToUse = new List<DrawWithDepth>(_drawObjects);
        DoAStep(drawObjectsToUse);
    }

    public static void FixedUpdate()
    {
        var itemsToRemove = new List<DrawWithDepth>();
        foreach (var item in _drawObjects)
        {
            if (item is GamemakerObject gm)
            {
                if (gm.Destroyed || gm.Marked)
                {
                    itemsToRemove.Add(item);
                }

                if (!InstanceManager.instance_exists_instanceid(gm.instanceId))
                {
                    // Benign, self-healing: RemoveMarked() (end of this method's own previous call)
                    // can be skipped for a frame that early-returns into a room change before
                    // reaching it (e.g. a death that immediately triggers room_goto/room_restart) --
                    // the marked instance is caught and cleaned up here on the next frame instead.
                    gm.Destroyed = true;
                    gm.Marked = true;
                    itemsToRemove.Add(item);
                }
            }
        }
        _drawObjects.RemoveAll(x => itemsToRemove.Contains(x));

        // Snapshot for the STEP only: an instance created this frame must not also get a Step this
        // frame. (Verified -- a probe project logs `D draw t=0` on the frame objD is created, i.e.
        // its Step counter is still 0 by the time it first draws.) The DRAW list is built separately
        // below, after the step, precisely because it must NOT be this snapshot.
        var stepObjects = new List<DrawWithDepth>(_drawObjects);

        GraphicsManager.PushMessage("DoAStep");
        var stepChangedRoom = DoAStep(stepObjects);
        if (stepChangedRoom)
        {
            return;
        }
        GraphicsManager.PopMessage();

        // Once per step (not per-view -- these are room-level, not tied to any one view), matching
        // real GM applying hspeed/vspeed to each room background's scroll offset every frame.
        foreach (var background in RoomManager.CurrentRoom.OldBackgrounds)
        {
            background?.UpdateOffset();
        }

        /*
         * https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/yyRoom.js#L4168
         * yyRoom.prototype.Draw
         */

        // Materialized once (.ToList()) rather than left lazy: RunDrawScript is called ~8x per
        // frame (PreDraw, DrawBegin/Draw/DrawEnd per view, PostDraw, DrawGUIBegin/GUI/End) and each
        // foreach over a lazy IOrderedEnumerable would re-run the full depth+instanceId sort and
        // re-allocate every single time. Sorting once per frame also matches real GameMaker, which
        // establishes the frame's draw order up front rather than re-sorting between draw phases.
        // OrderBy is a stable sort, so anything DrawOrderComparer calls equal keeps registration
        // order.
        // Built from the LIVE list, after the step -- not from the pre-step snapshot above.
        //
        // GameMaker draws an instance on the frame it was created: a probe project logs
        // `D CREATED` and then `D draw t=0` in the same frame, before the next step begins. Drawing
        // a pre-step snapshot instead delayed every new instance by a frame, which is invisible for
        // anything that lives a while and fatal for anything that doesn't.
        //
        // I Wanna Spook Jam's Matterclaw boss is the fatal case: obj_128MatterclawHand's End Step
        // creates the five connector circles joining it to the head *every frame*, and each one
        // destroys itself in its animation-end event. They never survived to a second frame, so the
        // only reason they were ever on screen was the snapshot still holding the previous frame's
        // (by then destroyed) copies. Once destroyed instances stopped drawing, the circles vanished
        // outright. Both halves are the same mistake: this list has to be what exists *now*.
        var drawList = _drawObjects.OrderBy(x => x, DrawOrderComparer.Instance).ToList();

        /*
         * PreDraw
         */
        GraphicsManager.PushMessage("PreDraw");
        var fbsize = CustomWindow.Instance.FramebufferSize;
        GraphicsManager.SetViewPort(0, 0, fbsize.X, fbsize.Y);
        GraphicsManager.SetViewArea(0, 0, fbsize.X, fbsize.Y, 0);

        if (CustomWindow.Instance != null) // only null in tests
        {
            GraphicsManager.FlushPolygonBatch();
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        var preDrawChangedRoom = RunDrawScript(drawList, EventSubtypeDraw.PreDraw);
        if (preDrawChangedRoom)
        {
            return;
        }
        GraphicsManager.PopMessage();

        SurfaceManager.SetApplicationSurface();

        if (SurfaceManager.UsingAppSurface)
        {
            if (CustomWindow.Instance != null) // only null in tests
            {
                // Real GM only clears at all if the room's "draw background color" option is on --
                // when it's off, whatever was drawn last frame is left showing through (a real,
                // occasionally-used trail/smear effect), and clears to the room's OWN configured
                // color rather than whatever ClearColor happened to be last set to (this previously
                // just cleared unconditionally to a stale/arbitrary color, ignoring both the room's
                // actual background_color and DrawBackgroundColor entirely).
                if (RoomManager.CurrentRoom.DrawBackgroundColor)
                {
                    GraphicsManager.FlushPolygonBatch();
                    GL.ClearColor(RoomManager.CurrentRoom.BackgroundColor.ABGRToCol4());
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                }
            }
        }

        /*
         * DrawViews
         */
        GraphicsManager.PushMessage("DrawViews");

        /*
         * UpdateViews
         */
        UpdateViews();


        if (RoomManager.CurrentRoom.RoomAsset.EnableViews)
        {
            for (var i = 0; i < 8; i++)
            {
                ViewCurrent = i;
                CurrentRenderingView = RoomManager.CurrentRoom.Views[i];

                if (!CurrentRenderingView.Visible)
                {
                    continue;
                }

                GraphicsManager.PushMessage($"draw view {i}");

                if (CurrentRenderingView.SurfaceId != -1)
                {
                    SurfaceManager.surface_set_target(CurrentRenderingView.SurfaceId);

                    // idk what the deal with scaled port stuff is. happens in both html5 and cpp

                    // ignore viewport, use entire surface. idk why
                }
                else
                {
                    // BUG: this stretches. idk why
                    /*
                    GraphicsManager.SetViewPort(
                        ViewportManager.CurrentRenderingView.PortPosition.X,// * displayScaleX,
                        ViewportManager.CurrentRenderingView.PortPosition.Y,// * displayScaleY,
                        ViewportManager.CurrentRenderingView.PortSize.X,// * displayScaleX,
                        ViewportManager.CurrentRenderingView.PortSize.Y// * displayScaleY
                    );
                    */
                }

                GraphicsManager.SetViewArea(
                    CurrentRenderingView.ViewX,
                    CurrentRenderingView.ViewY,
                    CurrentRenderingView.ViewW,
                    CurrentRenderingView.ViewH,
                    CurrentRenderingView.Angle
                );

                // ROOM BACKGROUNDS
                // this is for undertale, this is definitely in the wrong place. just putting it here to get it drawing.
                foreach (var item in RoomManager.CurrentRoom.OldBackgrounds)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    item.Draw();
                }

                /*
                 * DrawTheRoom
                 */
                GraphicsManager.PushMessage("DrawTheRoom");
                if (RunDrawScript(drawList, EventSubtypeDraw.DrawBegin))
                {
                    break;
                }

                if (RunDrawScript(drawList, EventSubtypeDraw.Draw))
                {
                    break;
                }

                if (RunDrawScript(drawList, EventSubtypeDraw.DrawEnd))
                {
                    break;
                }
                GraphicsManager.PopMessage();

                if (DebugBBoxes)
                {
                    foreach (var item in drawList)
                    {
                        if (item is GamemakerObject gm && gm.Active)
                        {
                            var color = new Color4(1.0f, 0.0f, 0.0f, 1.0f);
                            var fill = new Color4(1.0f, 0.0f, 0.0f, 0.05f);

                            if (gm.mouse_over)
                            {
                                color.R = fill.R = 0.0f;
                                color.G = fill.G = 1.0f;
                                color.B = fill.B = 0.0f;
                            }

                            var vertices = new Vector3d[] {
                                new(gm.bbox.left, gm.bbox.top, GraphicsManager.GR_Depth),
                                new(gm.bbox.right, gm.bbox.top, GraphicsManager.GR_Depth),
                                new(gm.bbox.right, gm.bbox.bottom, GraphicsManager.GR_Depth),
                                new(gm.bbox.left, gm.bbox.bottom, GraphicsManager.GR_Depth)
                            };

                            CustomWindow.Draw(new GMPolygonJob()
                            {
                                Colors = [color, color, color, color],
                                Vertices = vertices,
                                Outline = true
                            });

                            CustomWindow.Draw(new GMPolygonJob()
                            {
                                Colors = [fill, fill, fill, fill],
                                Vertices = vertices,
                                Outline = false
                            });
                        }
                    }
                }

                if (CurrentRenderingView.SurfaceId != -1)
                {
                    SurfaceManager.surface_reset_target();
                }

                GraphicsManager.PopMessage(); // draw view
            }
        }
        else // views not enabled
        {
            GraphicsManager.PushMessage("draw viewless");

            GraphicsManager.SetViewPort(0, 0, SurfaceManager.ApplicationWidth, SurfaceManager.ApplicationHeight);
            GraphicsManager.SetViewArea(0, 0, RoomManager.CurrentRoom.SizeX, RoomManager.CurrentRoom.SizeY, 0);

            // dummy view for full room rendering
            // i think this is mostly for tiled rendering, which should switch to using room extents
            CurrentRenderingView = new()
            {
                ViewX = 0,
                ViewY = 0,
                ViewW = RoomManager.CurrentRoom.SizeX,
                ViewH = RoomManager.CurrentRoom.SizeY,
                PortSize = new(SurfaceManager.ApplicationWidth, SurfaceManager.ApplicationHeight)
            };

            // ROOM BACKGROUNDS
            // A room's backgrounds have nothing to do with whether it uses views -- real GM draws
            // them either way. Only the views-enabled branch above did, so a room with views
            // switched off rendered with no background at all, leaving whatever the application
            // surface was cleared to showing through: in Continuity's first two NANG rooms that's
            // the untouched GameMaker default room colour (0xC0C0C0), i.e. a grey screen where the
            // near-black bNang background belongs.
            foreach (var item in RoomManager.CurrentRoom.OldBackgrounds)
            {
                if (item == null)
                {
                    continue;
                }

                item.Draw();
            }

            /*
             * DrawTheRoom
             */
            GraphicsManager.PushMessage("DrawTheRoom");
            if (RunDrawScript(drawList, EventSubtypeDraw.DrawBegin))
            {
                return;
            }

            if (RunDrawScript(drawList, EventSubtypeDraw.Draw))
            {
                return;
            }

            if (RunDrawScript(drawList, EventSubtypeDraw.DrawEnd))
            {
                return;
            }
            GraphicsManager.PopMessage();
            GraphicsManager.PopMessage();
        }

        GraphicsManager.PopMessage(); // draw views

        if (SurfaceManager.UsingAppSurface)
        {
            SurfaceManager.surface_reset_target();
        }

        if (SurfaceManager.SurfaceStack.Count != 0)
        {
            DebugLog.LogError("Unbalanced surface stack. You MUST use surface_reset_target() for each set.");
            // BUG: room transitions become unbalanced. probably because of early returns above
            while (SurfaceManager.SurfaceStack.Count != 0)
            {
                SurfaceManager.surface_reset_target();
            }
            return;
        }

        CurrentRenderingView = null;

        /*
         * PostDraw
         */
        GraphicsManager.PushMessage("PostDraw");
        GraphicsManager.SetViewPort(0, 0, fbsize.X, fbsize.Y);
        GraphicsManager.SetViewArea(0, 0, fbsize.X, fbsize.Y, 0);

        var postDrawChangedRoom = RunDrawScript(drawList, EventSubtypeDraw.PostDraw);
        if (postDrawChangedRoom)
        {
            return;
        }
        GraphicsManager.PopMessage();

        /*
         * DrawApplicationSurface
         */
        GraphicsManager.PushMessage("DrawApplicationSurface");
        if (SurfaceManager.AppSurfaceAutodraw && SurfaceManager.UsingAppSurface)
        {
            // gamemaker actually uses alpha test enable here, and saves/restores the state. just change it to that when this breaks
            GraphicsManager.FlushPolygonBatch();
            GL.Disable(EnableCap.Blend);
            SurfaceManager.draw_surface_stretched(SurfaceManager.application_surface, 0, 0, fbsize.X, fbsize.Y);
            GL.Enable(EnableCap.Blend);
        }
        GraphicsManager.PopMessage();

        // TODO: calc gui transform? and port stuff?? idk what this is lol
        // and update room extents to that i guess

        /*
         * DrawGUI
         */
        if (ShouldDrawGui)
        {
            GraphicsManager.PushMessage("DrawGUI");
            if (GuiSize is Vector2i vec)
            {
                GraphicsManager.SetViewArea(0, 0, vec.X, vec.Y, 0);
            }

            if (RunDrawScript(drawList, EventSubtypeDraw.DrawGUIBegin))
            {
                return;
            }

            if (RunDrawScript(drawList, EventSubtypeDraw.DrawGUI))
            {
                return;
            }

            if (RunDrawScript(drawList, EventSubtypeDraw.DrawGUIEnd))
            {
                return;
            }

            GraphicsManager.SetViewArea(0, 0, fbsize.X, fbsize.Y, 0);
            GraphicsManager.PopMessage();
        }

        if (RoomManager.CurrentRoom != null)
        {
            RoomManager.CurrentRoom.RemoveMarked();
        }

        //GamemakerCamera.Instance.GetComponent<Camera>().Render();
    }

    public static void UpdateViews()
    {
        if (!RoomManager.CurrentRoom.RoomAsset.EnableViews)
        {
            return;
        }

        for (var i = 0; i < 8; i++)
        {
            var view = RoomManager.CurrentRoom.Views[i];

            if (!view.Visible)
            {
                continue;
            }

            if (view.Camera == null)
            {
                continue;
            }

            view.Camera.Update();
        }

        /*
        var left = 999999;
        var right = -999999;
        var top  = 999999;
        var bottom = -999999;

        for (var i = 0; i < 8; i++)
        {
            var view = RoomManager.CurrentRoom.Views[i];

            if (view.Visible )// && pView.surface_id==-1)
            {
                if( left>view.PortPosition.X) left = view.PortPosition.X;
                if( right<(view.PortPosition.X+view.PortSize.X) ) right= view.PortPosition.X+view.PortSize.X;
                if( top>view.PortPosition.Y) top = view.PortPosition.Y;
                if( bottom<(view.PortPosition.Y+view.PortSize.Y) ) bottom = view.PortPosition.Y+view.PortSize.Y;
            }
        }

        var displayScaleX = SurfaceManager.ApplicationWidth /  (right-left);
        var displayScaleY = SurfaceManager.ApplicationHeight /  (bottom-top);
        */
    }

    public static void HandleKeyboard()
    {
        KeyDown();
        KeyPressed();
        KeyReleased();
    }

    public static void KeyDown()
    {
        var keyDown = 0;
        for (var i = 0; i < 256; i++)
        {
            if (InputHandler.KeyDown[i])
            {
                keyDown = 1;
                Handle(i, EventType.Keyboard);
            }
        }

        // either 0 (no key) or 1 (any key)
        Handle(keyDown, EventType.Keyboard);
    }

    public static void KeyPressed()
    {
        var keyPressed = 0;
        for (var i = 0; i < 256; i++)
        {
            if (InputHandler.KeyPressed[i])
            {
                keyPressed = 1;
                Handle(i, EventType.KeyPress);
            }
        }

        // either 0 (no key) or 1 (any key)
        Handle(keyPressed, EventType.KeyPress);
    }

    public static void KeyReleased()
    {
        var keyReleased = 0;
        for (var i = 0; i < 256; i++)
        {
            if (InputHandler.KeyReleased[i])
            {
                keyReleased = 1;
                Handle(i, EventType.KeyRelease);
            }
        }

        // either 0 (no key) or 1 (any key)
        Handle(keyReleased, EventType.KeyRelease);
    }

    public static void HandleMouse()
    {
        // `InstanceManager.instances.Keys.ElementAt(i)` inside this loop used to re-walk the
        // dictionary's key enumerator from the start on every single iteration (LINQ's ElementAt
        // has no fast path for IDictionary.KeyCollection, which isn't index-addressable) --
        // O(n^2) in instance count, every single step, for every game, regardless of whether any
        // instance actually has mouse scripts. A one-time snapshot into an array (still safe
        // against a mouse-enter/leave script below creating/destroying instances mid-loop, same
        // as the original indexed-by-key approach was defending against, just without paying for
        // it n times) makes this O(n).
        var instanceSnapshot = InstanceManager.instances.Values.ToArray();

        var mouseX = GraphicFunctions.window_views_mouse_get_x([]).Conv<double>();
        var mouseY = GraphicFunctions.window_views_mouse_get_y([]).Conv<double>();

        for (var i = 0; i < instanceSnapshot.Length; i++)
        {
            var inst = instanceSnapshot[i];

            if (inst.Definition.MouseScripts.Count == 0)
            {
                continue;
            }

            if (CollisionManager.Collision_Point(inst, mouseX, mouseY, false))
            {
                MouseDown(inst);
                MousePressed(inst);
                MouseReleased(inst);

                if (!inst.mouse_over)
                {
                    inst.mouse_over = true;
                    GamemakerObject.ExecuteEvent(inst, inst.Definition, EventType.Mouse, (int)EventSubtypeMouse.MouseEnter);
                }
            }
            else
            {
                if (inst.mouse_over)
                {
                    inst.mouse_over = false;
                    GamemakerObject.ExecuteEvent(inst, inst.Definition, EventType.Mouse, (int)EventSubtypeMouse.MouseLeave);
                }
            }
        }

        MouseDown();
        MousePressed();
        MouseReleased();

        // TODO: mouse wheel
    }

    public static void MouseDown(GamemakerObject? inst = null)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!InputHandler.MouseDown[i])
            {
                continue;
            }

            if (inst is not null)
            {
                GamemakerObject.ExecuteEvent(inst, inst.Definition, EventType.Mouse, (int)EventSubtypeMouse.LeftButton + i);
            }
            else
            {
                Handle((int)EventSubtypeMouse.GlobLeftButton + i, EventType.Mouse);
            }
        }
    }

    public static void MousePressed(GamemakerObject? inst = null)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!InputHandler.MousePressed[i])
            {
                continue;
            }

            if (inst is not null)
            {
                GamemakerObject.ExecuteEvent(inst, inst.Definition, EventType.Mouse, (int)EventSubtypeMouse.LeftPressed + i);
            }
            else
            {
                Handle((int)EventSubtypeMouse.GlobLeftPressed + i, EventType.Mouse);
            }
        }
    }

    public static void MouseReleased(GamemakerObject? inst = null)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!InputHandler.MouseReleased[i])
            {
                continue;
            }

            if (inst is not null)
            {
                GamemakerObject.ExecuteEvent(inst, inst.Definition, EventType.Mouse, (int)EventSubtypeMouse.LeftReleased + i);
            }
            else
            {
                Handle((int)EventSubtypeMouse.GlobLeftReleased + i, EventType.Mouse);
            }
        }
    }

    public static void Handle(int key, EventType type)
    {
        var objects = InstanceManager.instances.Values.ToList();
        foreach (var obj in objects)
        {
            GamemakerObject.ExecuteEvent(obj, obj.Definition, type, key);
        }
    }
}
