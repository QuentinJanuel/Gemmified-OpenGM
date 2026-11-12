using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// Two bugs that showed up together in I Wanna Spook Jam's dribix witch boss, one causing the other.
///
/// scrDB_PreFire is `with (oDB_Bullet) { ... round(move_towards_point(xxx, yyy, 10)); ... }`.
/// move_towards_point returned C# null, which the VM reads as GML undefined, so round() threw --
/// but in GameMaker a builtin that "returns nothing" still pushes a value, and that value is 0
/// (OpenGMK's kernel.rs returns Ok(Default::default()) == Real(0) from all 558 of them).
///
/// The throw then escaped its own code. ExecuteCode pops exactly one EnvStack frame when it
/// returns, which is right only when the `with` block's frames (a null marker plus one per
/// instance) were already unwound by its POPENV. Breaking out of the instruction loop on a failure
/// never reaches that POPENV, so the single pop took a stranded bullet frame instead, and the
/// witch's Step event carried on running as a bullet -- which is exactly what the log showed:
///
///     Variable inCharge doesn't exist in 129308 oDB_Bullet, pushing undefined.
///      - gml_Object_oDB_Witch_Step_0 | oDB_Witch (Obj:270, Inst:106004) | @ Ins:2234
///
/// inCharge/Candy/roll are the witch's own Create-event variables. An error is bad enough; it has
/// to at least stay local to the code it happened in.
/// </summary>
[TestClass]
public class VMErrorUnwindTests
{
    private const int BulletId = 268; // oDB_Bullet
    private const int WitchId = 270;  // oDB_Witch

    private ObjectDefinition _bullet = null!;
    private ObjectDefinition _witch = null!;

    [TestInitialize]
    public void SetUp()
    {
        ScriptResolver.InitGMLFunctions();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = 1;
        VMExecutor.EnvStack.Clear();
        VMExecutor.CallStack.Clear();

        _bullet = Define(BulletId, "oDB_Bullet");
        _witch = Define(WitchId, "oDB_Witch");

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

        for (var id = FirstCodeAssetId; id < CodeAssetId; id++)
        {
            GameLoader.Codes.Remove(id);
        }

        CodeAssetId = FirstCodeAssetId;
    }

    private static ObjectDefinition Define(int assetId, string name)
    {
        var def = new ObjectDefinition { AssetId = assetId, Name = name };
        InstanceManager.ObjectDefinitions.Add(assetId, def);
        return def;
    }

    private static GamemakerObject Spawn(ObjectDefinition def, double x, double y)
        => new(def, x, y, def.depth, InstanceManager.NextInstanceID++, -1, true, false, -1);

    private static VMCode Code(string name, string asm)
    {
        var code = GameConverter.ConvertAssembly(null, asm);
        code.Name = name;

        // Failing is the point of most of these, and DebugLog.PrintCallStack looks every frame's
        // code up in GameLoader.Codes by name to report the label it died on.
        code.AssetId = CodeAssetId++;
        GameLoader.Codes[code.AssetId] = code;

        return code;
    }

    private const int FirstCodeAssetId = 90_000;
    private static int CodeAssetId = FirstCodeAssetId;

    /// <summary>
    /// scrDB_PreFire's shape: `with (oDB_Bullet) { &lt;something that fails&gt; }`. An unresolvable
    /// call is the cleanest failure to use here -- it takes the same `break` out of the instruction
    /// loop as a thrown exception, without depending on the undefined handling being tested
    /// separately below.
    /// </summary>
    private static VMCode WithBlockThatFails() => Code("scrDB_PreFire", $"""
        :[0]
        pushi.e {BulletId}
        pushenv [2]
        :[1]
        call.i noSuchFunctionExists(argc=0)
        popz.v
        :[2]
        popenv [1]
        :[3]
        ret.v
        """);

    [TestMethod]
    public void AFailureInsideAWithBlockDoesntStrandItsFrames()
    {
        Spawn(_bullet, 0, 0);
        Spawn(_bullet, 32, 0);

        var depthBefore = VMExecutor.EnvStack.Count;

        VMExecutor.ExecuteCode(WithBlockThatFails(), Spawn(_witch, 100, 100));

        // One `with` over two instances strands a null marker plus two frames on the way out.
        Assert.AreEqual(depthBefore, VMExecutor.EnvStack.Count,
            "everything the code pushed has to be gone again by the time it returns");
    }

    [TestMethod]
    public void TheCallerKeepsItsOwnSelfAfterACalleeFails()
    {
        // The actual bug, in the shape the log showed it: oDB_Witch's Step event calls
        // scrDB_PreFire, that fails inside its `with (oDB_Bullet)`, and the witch's remaining code
        // runs with a bullet as `self` -- so every read of the witch's own variables misses.
        Spawn(_bullet, 0, 0);
        Spawn(_bullet, 32, 0);
        var witch = Spawn(_witch, 100, 100);

        // Stand in for gml_Object_oDB_Witch_Step_0's own frame, which is what the callee returns to.
        VMExecutor.EnvStack.Push(new VMEnvFrame { Self = witch, ObjectDefinition = _witch });
        var callerDepth = VMExecutor.EnvStack.Count;

        VMExecutor.ExecuteCode(WithBlockThatFails(), witch);

        Assert.AreEqual(callerDepth, VMExecutor.EnvStack.Count, "the caller's frame is back on top");
        Assert.AreSame(witch, VMExecutor.Self.Self, "the caller is still itself, not one of the bullets");
    }

    [TestMethod]
    public void AWithBlockThatFinishesNormallyIsStillBalanced()
    {
        // The control: nothing about the ordinary path changes, including running the body once per
        // instance and leaving the caller alone afterwards.
        var ran = 0;
        var body = Code("with-body", $"""
            :[0]
            pushi.e {BulletId}
            pushenv [2]
            :[1]
            push.v self.id
            popz.v
            :[2]
            popenv [1]
            :[3]
            push.d 0
            conv.d.v
            ret.v
            """);
        body.OnCodeExecuted += () => ran++;

        Spawn(_bullet, 0, 0);
        Spawn(_bullet, 32, 0);
        var witch = Spawn(_witch, 100, 100);

        VMExecutor.EnvStack.Push(new VMEnvFrame { Self = witch, ObjectDefinition = _witch });
        var callerDepth = VMExecutor.EnvStack.Count;

        VMExecutor.ExecuteCode(body, witch);

        Assert.AreEqual(1, ran, "sanity check: the code actually ran");
        Assert.AreEqual(callerDepth, VMExecutor.EnvStack.Count);
        Assert.AreSame(witch, VMExecutor.Self.Self);
    }

    [TestMethod]
    public void MoveTowardsPointReturnsZeroRatherThanUndefined()
    {
        var bullet = Spawn(_bullet, 0, 0);

        var result = VMExecutor.ExecuteCode(Code("move", """
            :[0]
            push.d 10
            conv.d.v
            push.d 0
            conv.d.v
            push.d 100
            conv.d.v
            call.i move_towards_point(argc=3)
            ret.v
            """), bullet);

        Assert.IsNotNull(result, "returned undefined -- which is what makes round() throw");
        Assert.AreEqual(0d, result.Conv<double>(), "GameMaker pushes 0, not undefined");
        Assert.AreEqual(10d, bullet.speed, 1e-9, "and it still did its actual job");
        Assert.AreEqual(0d, bullet.direction, 1e-9, "straight right, towards (100, 0)");
    }

    [TestMethod]
    public void RoundingTheResultOfMoveTowardsPointDoesntThrow()
    {
        // Verbatim from scrDB_PreFire. Nothing reads the value -- the author just wrapped a void
        // call -- so all that matters is that the enclosing code survives to run its next line.
        var bullet = Spawn(_bullet, 0, 0);

        var result = VMExecutor.ExecuteCode(Code("scrDB_PreFire-line-12", """
            :[0]
            push.d 10
            conv.d.v
            push.d 0
            conv.d.v
            push.d 100
            conv.d.v
            call.i move_towards_point(argc=3)
            call.i round(argc=1)
            ret.v
            """), bullet);

        Assert.IsNotNull(result, "round() aborted the whole code, so nothing was returned");
        Assert.AreEqual(0d, result.Conv<double>(), "round(move_towards_point(...)) is round(0)");
        Assert.AreEqual(10d, bullet.speed, 1e-9);
    }
}
