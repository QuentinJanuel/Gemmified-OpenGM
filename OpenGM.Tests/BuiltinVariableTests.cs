using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;

namespace OpenGM.Tests;

/// <summary>
/// Built-in *variables* that were listed in VariableResolver but left without a getter, or not
/// listed at all. Both shapes fail differently and both took an event with them in I Wanna Spook
/// Jam's Quentin stage:
///
///   Execution of instruction push.v [array]self.instance_id ... in script
///   gml_Object_Quentin_objSpotlight_Create_0 failed :
///   System.NotImplementedException: Built in variable instance_id not implemented.
///
///   Variable current_year doesn't exist in 117661 Quentin_objSpikeUp, pushing undefined.
///
/// A registered-but-getterless variable throws and aborts the whole event (the spotlight never
/// finished its Create, so it never sorted itself behind the things it lights). An unregistered one
/// is quieter and worse: it falls through to "this instance has no such variable", pushes undefined,
/// and the comparison built on it silently takes the wrong branch -- objSpikeUp gates a secret on
/// <c>current_year > year || ...</c>, which with undefined just decides "no secret" and says nothing.
/// </summary>
[TestClass]
public class BuiltinVariableTests
{
    private const int ObjId = 7700;

    [TestInitialize]
    public void SetUp()
    {
        ScriptResolver.InitGMLFunctions();
        InstanceManager.ClearAllInstances();
        InstanceManager.ObjectDefinitions.Clear();
        InstanceManager.NextInstanceID = GMConstants.FIRST_INSTANCE_ID;
        InstanceManager.ObjectDefinitions[ObjId] = new ObjectDefinition { AssetId = ObjId, Name = "objBuiltinVarTest" };
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
    }

    private static GamemakerObject Spawn()
    {
        var def = InstanceManager.ObjectDefinitions[ObjId];
        return new GamemakerObject(def, 0, 0, def.depth, InstanceManager.NextInstanceID++, -1, true, false, -1);
    }

    /// <summary>
    /// The exact failure mode: a name present in the table with a null getter throws out of
    /// PushBuiltin/PushBuiltinArrayIndex rather than resolving, killing the event.
    /// </summary>
    [TestMethod]
    public void TheseBuiltinVariablesAllHaveGetters()
    {
        foreach (var name in new[]
                 {
                     "instance_id", "instance_count",
                     "current_time", "current_year", "current_month", "current_day",
                     "current_weekday", "current_hour", "current_minute", "current_second"
                 })
        {
            Assert.IsTrue(VariableResolver.BuiltInVariables.ContainsKey(name), $"{name} is not registered at all");
            Assert.IsNotNull(VariableResolver.BuiltInVariables[name].getter, $"{name} is registered with no getter");
        }
    }

    [TestMethod]
    public void InstanceIdIndexesTheActiveInstances()
    {
        var a = Spawn();
        var b = Spawn();
        var c = Spawn();

        Assert.AreEqual(3, VariableResolver.get_instance_count(0).Conv<int>());

        var ids = new[] { 0, 1, 2 }.Select(i => VariableResolver.get_instance_id(i).Conv<int>()).ToList();

        CollectionAssert.AreEquivalent(
            new[] { a.instanceId, b.instanceId, c.instanceId },
            ids,
            "every active instance is reachable through instance_id exactly once");
    }

    [TestMethod]
    public void InstanceIdPastTheEndIsNooneRatherThanAThrow()
    {
        Spawn();
        Spawn();

        // The idiom this exists for: `for (a = 0; a <= instance_count; a += 1)` runs one index past
        // the last instance every single time and leans on instance_exists to reject the result.
        // Quentin_objSpotlight is written exactly that way, so throwing here kills its Create event.
        var count = VariableResolver.get_instance_count(0).Conv<int>();

        Assert.AreEqual(GMConstants.noone, VariableResolver.get_instance_id(count).Conv<int>(),
            "reading at instance_count (one past the end) gives noone");
        Assert.AreEqual(GMConstants.noone, VariableResolver.get_instance_id(count + 50).Conv<int>());
        Assert.AreEqual(GMConstants.noone, VariableResolver.get_instance_id(-1).Conv<int>());
    }

    [TestMethod]
    public void InstanceIdSkipsDeactivatedInstances()
    {
        var kept = Spawn();
        var deactivated = Spawn();

        // instance_deactivate_* moves an instance out of `instances` and into `inactiveInstances`.
        InstanceManager.instances.Remove(deactivated.instanceId);
        InstanceManager.inactiveInstances[deactivated.instanceId] = deactivated;

        Assert.AreEqual(1, VariableResolver.get_instance_count(0).Conv<int>());
        Assert.AreEqual(kept.instanceId, VariableResolver.get_instance_id(0).Conv<int>());
        Assert.AreEqual(GMConstants.noone, VariableResolver.get_instance_id(1).Conv<int>(),
            "the deactivated instance isn't indexable, matching what instance_count reports");

        InstanceManager.inactiveInstances.Remove(deactivated.instanceId);
        InstanceManager.instances[deactivated.instanceId] = deactivated;
    }

    [TestMethod]
    public void TheDateVariablesReadTheClock()
    {
        var now = DateTime.Now;

        Assert.AreEqual(now.Year, VariableResolver.get_current_year(0).Conv<int>());
        Assert.AreEqual(now.Month, VariableResolver.get_current_month(0).Conv<int>());
        Assert.AreEqual(now.Day, VariableResolver.get_current_day(0).Conv<int>());
        Assert.AreEqual(now.Hour, VariableResolver.get_current_hour(0).Conv<int>());

        // objSpikeUp's actual test: current_year > 2016 || (current_month > 10 || (current_month ==
        // 10 && current_day >= 31)). Undefined on any of these threw the comparison off silently.
        Assert.IsTrue(VariableResolver.get_current_year(0).Conv<int>() > 2016,
            "the Halloween-secret gate in Quentin_objSpikeUp compares against 2016");
    }

    [TestMethod]
    public void CurrentWeekdayIsANumberNotAnEnum()
    {
        var weekday = VariableResolver.get_current_weekday(0);

        // Was returning DateTime.DayOfWeek itself -- a boxed C# enum where every other date variable
        // gives a number. Date.cs already casts for date_get_weekday; GM numbers 0 = Sunday too.
        Assert.IsInstanceOfType(weekday, typeof(int), "GML sees a number, not a boxed DayOfWeek");
        Assert.AreEqual((int)DateTime.Now.DayOfWeek, weekday.Conv<int>());
    }
}
