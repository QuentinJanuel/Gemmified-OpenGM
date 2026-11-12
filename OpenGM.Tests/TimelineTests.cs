using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// Timelines -- a moment-keyed sequence of code blocks an instance plays back over time -- weren't
/// implemented at all: the converter wrote timeline *names* but never their moments, no instance
/// carried timeline_index/position/running state, and nothing stepped one per frame. The whole
/// action_timeline_* family was registered as a no-op stub, so a game driving a boss off a timeline
/// got a boss that never moved and, because a stub isn't an error, not one line of log to say why.
///
/// I Wanna Spook Jam's Erik castle miniboss is exactly that shape -- objErik_CherryBoss's entire
/// Create event is:
///
///     action_timeline_set(1, 0, 0, 0);
///     hp = 20;
///     image_speed = 0;
///
/// and every attack it makes lives in Timeline_CherryBoss's moments. The fight softlocked: the
/// music (started elsewhere) played, the cherry sat still forever, and nothing was logged.
///
/// Reference semantics, including the ordering quirks, are OpenGMK's Game::frame
/// (gm8emulator/src/game.rs) and its action_timeline_* in gml/kernel.rs.
/// </summary>
[TestClass]
public class TimelineTests
{
    private const int CherryBossId = 6001;
    private const int CherryTimelineId = 1; // what action_timeline_set(1, ...) resolves to

    private ObjectDefinition _cherryBoss = null!;
    private readonly List<string> _fired = new();

    private bool _roomLoadedBefore;

    [TestInitialize]
    public void SetUp()
    {
        ScriptResolver.InitGMLFunctions();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;
        VMExecutor.EnvStack.Clear();
        VMExecutor.CallStack.Clear();
        GameLoader.Timelines.Clear();
        _fired.Clear();

        // HandleTimeLine skips instances in a room that isn't loaded, the same way the Step event
        // dispatch does.
        _roomLoadedBefore = RoomManager.RoomLoaded;
        RoomManager.RoomLoaded = true;

        _cherryBoss = new ObjectDefinition { AssetId = CherryBossId, Name = "objErik_CherryBoss" };
        InstanceManager.ObjectDefinitions[CherryBossId] = _cherryBoss;
        InstanceManager.InitObjectMap();
    }

    [TestCleanup]
    public void TearDown()
    {
        foreach (var instance in InstanceManager.instances.Values.ToList())
        {
            DrawManager.Unregister(instance);
        }

        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        VMExecutor.EnvStack.Clear();
        VMExecutor.CallStack.Clear();
        GameLoader.Timelines.Clear();
        RoomManager.RoomLoaded = _roomLoadedBefore;

        for (var id = FirstCodeAssetId; id < CodeAssetId; id++)
        {
            GameLoader.Codes.Remove(id);
        }

        CodeAssetId = FirstCodeAssetId;

        foreach (var id in _scriptIds)
        {
            if (ScriptResolver.ScriptsByIndex.Remove(id, out var script))
            {
                ScriptResolver.ScriptsByName.Remove(script.Name);
            }
        }

        _scriptIds.Clear();

        // AssetIndexManager is static and survives the test, so anything timeline_add registered
        // has to come back out or the next test's generated names shift.
        foreach (var name in _registeredTimelineNames)
        {
            AssetIndexManager.Unregister(AssetType.timelines, name);
        }

        _registeredTimelineNames.Clear();
    }

    private readonly List<string> _registeredTimelineNames = new();

    private const int FirstCodeAssetId = 91_000;
    private static int CodeAssetId = FirstCodeAssetId;

    private VMCode Code(string label, string asm = """
        :[0]
        push.v self.id
        popz.v
        """)
    {
        var code = GameConverter.ConvertAssembly(null, asm);
        code.Name = label;
        code.AssetId = CodeAssetId++;
        GameLoader.Codes[code.AssetId] = code;
        code.OnCodeExecuted += () => _fired.Add(label);
        return code;
    }

    /// <summary>Registers a timeline whose moments are keyed to the given steps.</summary>
    private Timeline DefineTimeline(int assetId, string name, params int[] steps)
    {
        var timeline = new Timeline { AssetId = assetId, Name = name };

        foreach (var step in steps)
        {
            timeline.Moments[step] = [Code($"{name}_{step}")];
        }

        GameLoader.Timelines[assetId] = timeline;
        return timeline;
    }

    /// <summary>Registers a script so timeline_moment_add_script has something to point at.</summary>
    private VMScript DefineScript(int assetIndex, string name)
    {
        var script = new VMScript { AssetIndex = assetIndex, Name = name, CustomCode = Code(name) };
        ScriptResolver.ScriptsByIndex[assetIndex] = script;
        ScriptResolver.ScriptsByName[name] = script;
        _scriptIds.Add(assetIndex);
        return script;
    }

    private readonly List<int> _scriptIds = new();

    private GamemakerObject Spawn()
    {
        var instance = new GamemakerObject(_cherryBoss, 0, 0, _cherryBoss.depth,
            InstanceManager.NextInstanceID++, -1, true, false, -1);
        // The constructor files it under its object, which is where DrawManager.HandleTimeLine now
        // reads its instances from rather than being handed a list.
        instance._createRan = true;
        return instance;
    }

    /// <summary>objErik_CherryBoss's Create event, as the game actually compiles it.</summary>
    private VMCode BossCreateEvent(int timelineId, int position, int startOption, int loopOption)
        => Code("gml_Object_objErik_CherryBoss_Create_0", $"""
            :[0]
            pushi.e {loopOption}
            conv.i.v
            pushi.e {startOption}
            conv.i.v
            pushi.e {position}
            conv.i.v
            pushi.e {timelineId}
            conv.i.v
            call.i action_timeline_set(argc=4)
            popz.v
            """);

    private void RunFrames(GamemakerObject instance, int count)
    {
        for (var i = 0; i < count; i++)
        {
            DrawManager.HandleTimeLine();
        }
    }

    [TestMethod]
    public void TheBossCreateEventStartsItsTimeline()
    {
        var boss = Spawn();

        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 0, 0), boss);

        Assert.AreEqual(CherryTimelineId, boss.timeline_index);
        Assert.AreEqual(0, boss.timeline_position);
        // 0 is "start immediately" in the D&D dialog, so it has to read as running, not stopped.
        Assert.IsTrue(boss.timeline_running, "action_timeline_set's third argument is inverted");
        Assert.IsFalse(boss.timeline_loop);
        Assert.AreEqual(1, boss.timeline_speed, "an instance plays a timeline at 1 step/frame by default");
    }

    [TestMethod]
    public void AStartedTimelineRunsEachMomentOnTheFrameItsKeyedTo()
    {
        // The actual regression: with no timeline subsystem this instance did nothing, forever.
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3, 7);

        var boss = Spawn();
        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 0, 0), boss);
        _fired.Clear(); // drop the Create event's own entry

        RunFrames(boss, 8);

        // Moment N runs on frame N+1: frame 1 covers position 0->1 and so fires the moment at 0.
        CollectionAssert.AreEqual(
            new[] { "Timeline_CherryBoss_0", "Timeline_CherryBoss_3", "Timeline_CherryBoss_7" },
            _fired,
            "every moment runs exactly once, in step order");
        Assert.AreEqual(8, boss.timeline_position);
    }

    [TestMethod]
    public void AMomentRunsWithTheTimelinesOwnInstanceAsSelf()
    {
        // Timeline_CherryBoss_0 is `with (objErik_CherryBoss) { action_move(...) }` -- harmless if
        // self is already the boss, but the moment has to run *on* the playing instance for the
        // moments that touch fields directly (image_speed = 0.3 on the same line) to land.
        var timeline = new Timeline { AssetId = CherryTimelineId, Name = "Timeline_CherryBoss" };
        timeline.Moments[0] = [Code("moment_sets_a_field", """
            :[0]
            push.d 0.3
            conv.d.v
            pop.v.v self.image_speed
            """)];
        GameLoader.Timelines[CherryTimelineId] = timeline;

        var boss = Spawn();
        boss.image_speed = 0;
        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 0, 0), boss);

        RunFrames(boss, 1);

        Assert.AreEqual(0.3, boss.image_speed, 1e-9);
    }

    [TestMethod]
    public void AnUnstartedTimelineNeverRuns()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        var boss = Spawn();
        // start_option 1 == "do not start", the paused half of the same D&D action.
        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 1, 0), boss);
        _fired.Clear();

        RunFrames(boss, 8);

        Assert.IsFalse(boss.timeline_running);
        CollectionAssert.AreEqual(Array.Empty<string>(), _fired);
        Assert.AreEqual(0, boss.timeline_position, "a paused timeline doesn't advance either");
    }

    [TestMethod]
    public void ANonLoopingTimelineStopsAtItsLastMoment()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        var boss = Spawn();
        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 0, 0), boss);
        _fired.Clear();

        RunFrames(boss, 20);

        CollectionAssert.AreEqual(
            new[] { "Timeline_CherryBoss_0", "Timeline_CherryBoss_3" },
            _fired,
            "without loop set, running off the end fires nothing further");
    }

    [TestMethod]
    public void ALoopingTimelineStartsOverAfterItsLastMoment()
    {
        // objHopHeartTop does action_timeline_set(5, 0, 0, 1) -- the looping half of the same call.
        DefineTimeline(CherryTimelineId, "Timeline_tmHopUpDriftHeartTop", 0, 3);

        var boss = Spawn();
        VMExecutor.ExecuteCode(BossCreateEvent(CherryTimelineId, 0, 0, 1), boss);
        _fired.Clear();
        Assert.IsTrue(boss.timeline_loop);

        // Length is the last moment's step (3), so position wraps on the frame that takes it past 3
        // -- which is the same frame the moment at 3 runs on.
        RunFrames(boss, 4);
        CollectionAssert.AreEqual(
            new[] { "Timeline_tmHopUpDriftHeartTop_0", "Timeline_tmHopUpDriftHeartTop_3" }, _fired);
        Assert.AreEqual(0, boss.timeline_position, "past the last moment, a looping timeline rewinds");

        RunFrames(boss, 1);
        CollectionAssert.AreEqual(
            new[]
            {
                "Timeline_tmHopUpDriftHeartTop_0",
                "Timeline_tmHopUpDriftHeartTop_3",
                "Timeline_tmHopUpDriftHeartTop_0"
            },
            _fired,
            "the next lap starts from the first moment again");
    }

    [TestMethod]
    public void SettingTimelineIndexAndRunningDirectlyStartsIt()
    {
        // objHopUpSpikeCreep's Step event drives its timeline through the instance variables
        // rather than the D&D wrapper: `timeline_index = tmHopUpSpikeCreep; timeline_running = 1;`
        DefineTimeline(CherryTimelineId, "Timeline_tmHopUpSpikeCreep", 0);

        var boss = Spawn();
        VMExecutor.ExecuteCode(Code("gml_Object_objHopUpSpikeCreep_Step_0", $"""
            :[0]
            pushi.e {CherryTimelineId}
            conv.i.v
            pop.v.v self.timeline_index
            pushi.e 1
            conv.i.v
            pop.v.v self.timeline_running
            """), boss);
        _fired.Clear();

        Assert.AreEqual(CherryTimelineId, boss.timeline_index);
        Assert.IsTrue(boss.timeline_running);

        RunFrames(boss, 1);

        CollectionAssert.AreEqual(new[] { "Timeline_tmHopUpSpikeCreep_0" }, _fired);
    }

    [TestMethod]
    public void AnInstanceWithNoTimelineIsLeftAlone()
    {
        // timeline_index defaults to -1, and every instance in the room goes through HandleTimeLine
        // every frame -- so the "no timeline" path has to be silent, not a lookup failure.
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0);

        var boss = Spawn();
        _fired.Clear();

        RunFrames(boss, 4);

        Assert.AreEqual(-1, boss.timeline_index);
        CollectionAssert.AreEqual(Array.Empty<string>(), _fired);
    }

    [TestMethod]
    public void AMomentThatDestroysTheInstanceStopsTheRestOfThatFrame()
    {
        // A moment can end the fight (CherryBoss's does, via action_kill_object). Moments don't go
        // through ExecuteEvent, so its Marked check has to be repeated in the moment loop.
        var timeline = new Timeline { AssetId = CherryTimelineId, Name = "Timeline_CherryBoss" };
        timeline.Moments[0] = [Code("moment_destroys_self", """
            :[0]
            call.i instance_destroy(argc=0)
            popz.v
            """)];
        timeline.Moments[1] = [Code("moment_after_destroy")];
        GameLoader.Timelines[CherryTimelineId] = timeline;

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;
        boss.timeline_speed = 2; // wide enough a step that both moments fall in the same frame
        _fired.Clear();

        RunFrames(boss, 1);

        CollectionAssert.AreEqual(new[] { "moment_destroys_self" }, _fired,
            "the moment after the one that destroyed the instance doesn't run");
    }

    [TestMethod]
    public void EveryActionInOneMomentRunsInOrder()
    {
        // A moment holds a list, not a single code -- editor-authored ones compile to exactly one,
        // but timeline_moment_add_script appends to whatever is already at that step.
        var timeline = new Timeline { AssetId = CherryTimelineId, Name = "Timeline_CherryBoss" };
        timeline.Moments[0] = [Code("first"), Code("second"), Code("third")];
        GameLoader.Timelines[CherryTimelineId] = timeline;

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;
        _fired.Clear();

        RunFrames(boss, 1);

        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, _fired);
    }

    // ---- The timeline_* resource API ----

    [TestMethod]
    public void TimelineExistsAndNameReportTheLoadedTimelines()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        Assert.IsTrue(ResourceFunctions.timeline_exists([CherryTimelineId]).Conv<bool>());
        Assert.IsFalse(ResourceFunctions.timeline_exists([999]).Conv<bool>());
        Assert.AreEqual("Timeline_CherryBoss", ResourceFunctions.timeline_get_name([CherryTimelineId]).Conv<string>());
    }

    [TestMethod]
    public void TimelineSizeAndMaxMomentDescribeTheMoments()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3, 7);

        // size is how many moments have anything in them; max_moment is the last one's step.
        Assert.AreEqual(3, ResourceFunctions.timeline_size([CherryTimelineId]).Conv<int>());
        Assert.AreEqual(7, ResourceFunctions.timeline_max_moment([CherryTimelineId]).Conv<int>());

        // An empty timeline is 0/0 rather than an error -- that's the length UpdateTimeline uses.
        var empty = ResourceFunctions.timeline_add([]).Conv<int>();
        _registeredTimelineNames.Add(GameLoader.Timelines[empty].Name);
        Assert.AreEqual(0, ResourceFunctions.timeline_size([empty]).Conv<int>());
        Assert.AreEqual(0, ResourceFunctions.timeline_max_moment([empty]).Conv<int>());
    }

    [TestMethod]
    public void TimelineAddCreatesAPlayableTimelineWhenTheGameShippedNone()
    {
        // AssetIndexManager.Register used to throw on the first asset of a type the game has none
        // of, which is exactly the case a game calling timeline_add is in.
        Assert.AreEqual(0, GameLoader.Timelines.Count);

        var index = ResourceFunctions.timeline_add([]).Conv<int>();
        _registeredTimelineNames.Add(GameLoader.Timelines[index].Name);

        Assert.IsTrue(ResourceFunctions.timeline_exists([index]).Conv<bool>());
        // It has to be a real registered asset, not just a dictionary entry, or nothing else in the
        // engine can look it up by name.
        Assert.AreEqual(index, AssetIndexManager.GetIndex(AssetType.timelines, GameLoader.Timelines[index].Name));

        var script = DefineScript(7401, "scrTimelineMoment");
        ResourceFunctions.timeline_moment_add_script([index, 2, script.AssetIndex]);

        var boss = Spawn();
        boss.timeline_index = index;
        boss.timeline_running = true;
        _fired.Clear();

        RunFrames(boss, 3);

        CollectionAssert.AreEqual(new[] { "scrTimelineMoment" }, _fired,
            "a timeline built at runtime plays like any other");
    }

    [TestMethod]
    public void TimelineMomentAddScriptAppendsRatherThanReplaces()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0);
        var first = DefineScript(7402, "scrFirstAdded");
        var second = DefineScript(7403, "scrSecondAdded");

        // Both land on the same step as the editor-authored moment already there.
        ResourceFunctions.timeline_moment_add_script([CherryTimelineId, 0, first.AssetIndex]);
        ResourceFunctions.timeline_moment_add_script([CherryTimelineId, 0, second.AssetIndex]);

        Assert.AreEqual(1, ResourceFunctions.timeline_size([CherryTimelineId]).Conv<int>(),
            "adding to an existing step doesn't create a second moment");

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;
        _fired.Clear();

        RunFrames(boss, 1);

        CollectionAssert.AreEqual(
            new[] { "Timeline_CherryBoss_0", "scrFirstAdded", "scrSecondAdded" },
            _fired,
            "actions run in the order they were added, after the one already there");
    }

    [TestMethod]
    public void TimelineMomentClearRemovesJustThatStep()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        ResourceFunctions.timeline_moment_clear([CherryTimelineId, 0]);

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;
        _fired.Clear();

        RunFrames(boss, 8);

        CollectionAssert.AreEqual(new[] { "Timeline_CherryBoss_3" }, _fired);
    }

    [TestMethod]
    public void TimelineClearEmptiesItButKeepsIt()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        ResourceFunctions.timeline_clear([CherryTimelineId]);

        Assert.IsTrue(ResourceFunctions.timeline_exists([CherryTimelineId]).Conv<bool>(),
            "clear empties the moments, it doesn't delete the timeline");
        Assert.AreEqual(0, ResourceFunctions.timeline_size([CherryTimelineId]).Conv<int>());

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;
        _fired.Clear();

        RunFrames(boss, 8);

        CollectionAssert.AreEqual(Array.Empty<string>(), _fired);
    }

    [TestMethod]
    public void TimelineDeleteLeavesAnInstancePlayingItHarmlesslyStuck()
    {
        DefineTimeline(CherryTimelineId, "Timeline_CherryBoss", 0, 3);

        var boss = Spawn();
        boss.timeline_index = CherryTimelineId;
        boss.timeline_running = true;

        ResourceFunctions.timeline_delete([CherryTimelineId]);
        _fired.Clear();

        Assert.IsFalse(ResourceFunctions.timeline_exists([CherryTimelineId]).Conv<bool>());
        Assert.AreEqual(-1, AssetIndexManager.GetIndex(AssetType.timelines, "Timeline_CherryBoss"));

        // The instance still points at the dead index -- that has to be a silent no-op every frame,
        // not a lookup that throws once per instance per frame forever.
        RunFrames(boss, 8);

        CollectionAssert.AreEqual(Array.Empty<string>(), _fired);
    }

    [TestMethod]
    public void TheResourceFunctionsDontThrowOnATimelineThatDoesntExist()
    {
        // Every one of these is reachable with a bad index from ordinary GML, and a throw would
        // abort the whole calling event.
        Assert.IsFalse(ResourceFunctions.timeline_exists([999]).Conv<bool>());
        Assert.AreEqual(0, ResourceFunctions.timeline_size([999]).Conv<int>());
        Assert.AreEqual(0, ResourceFunctions.timeline_max_moment([999]).Conv<int>());
        ResourceFunctions.timeline_get_name([999]);
        ResourceFunctions.timeline_clear([999]);
        ResourceFunctions.timeline_delete([999]);
        ResourceFunctions.timeline_moment_clear([999, 0]);
        ResourceFunctions.timeline_moment_add_script([999, 0, 0]);
    }
}
