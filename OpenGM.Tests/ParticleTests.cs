using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Particles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The whole particle pipeline used to be scaffolding -- part_emitter_burst was a stub and nothing
/// ever aged or moved a particle -- so these go through the GML entry points rather than calling
/// <see cref="ParticleManager"/> directly, and assert against the particles that come out the far
/// end. The burst sequence in <see cref="BurstEmitsParticlesInsideTheEmitterRegion"/> is the one
/// real games use (create emitter, set region, burst, destroy emitter).
///
/// These don't draw, so nothing here needs a GL context.
/// </summary>
[TestClass]
public class ParticleTests
{
    [TestInitialize]
    public void SetUp()
    {
        ParticleManager.Reset();

        // Nothing loads a game here, so the RNG state is still all zeros and every "random" draw
        // comes back 0 -- which would quietly turn every distribution below into a constant.
        GMRandom.InitRandom(12345);
    }

    [TestCleanup]
    public void TearDown() => ParticleManager.Reset();

    private static int CreateSystem() => ParticlesFunctions.part_system_create([]).Conv<int>();

    private static int CreateType(int lifeMin = 10, int lifeMax = 10)
    {
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, lifeMin, lifeMax]);
        return type;
    }

    private static List<Particle> Particles(int system) => ParticleManager.PartSystems[system].Particles;

    [TestMethod]
    public void BurstEmitsParticlesInsideTheEmitterRegion()
    {
        var system = CreateSystem();
        var type = CreateType();

        var emitter = ParticlesFunctions.part_emitter_create([system]).Conv<int>();
        ParticlesFunctions.part_emitter_region([system, emitter, 100, 110, 200, 220, 0, 0]);
        ParticlesFunctions.part_emitter_burst([system, emitter, type, 10]);
        ParticlesFunctions.part_emitter_destroy([system, emitter]);

        Assert.AreEqual(10, Particles(system).Count);

        foreach (var particle in Particles(system))
        {
            Assert.AreEqual(type, particle.PartType);
            Assert.IsTrue(particle.X is >= 100 and <= 110, $"x {particle.X} outside the emitter region");
            Assert.IsTrue(particle.Y is >= 200 and <= 220, $"y {particle.Y} outside the emitter region");
        }

        // They have to be spread across the region, not all stacked on one corner of it.
        Assert.IsTrue(Particles(system).Select(x => x.X).Distinct().Count() > 1);
        Assert.IsTrue(Particles(system).Select(x => x.Y).Distinct().Count() > 1);
    }

    [TestMethod]
    public void DestroyingAnEmitterLeavesItsAlreadyBurstParticlesAlone()
    {
        var system = CreateSystem();
        var type = CreateType();

        var emitter = ParticlesFunctions.part_emitter_create([system]).Conv<int>();
        ParticlesFunctions.part_emitter_region([system, emitter, 0, 0, 0, 0, 0, 0]);
        ParticlesFunctions.part_emitter_burst([system, emitter, type, 5]);
        ParticlesFunctions.part_emitter_destroy([system, emitter]);

        // A burst emitter is a throwaway -- games create one, burst, and destroy it in the same
        // line of GML, so the particles have to outlive it.
        Assert.AreEqual(5, Particles(system).Count);

        ParticleManager.UpdateAllSystems();

        Assert.AreEqual(5, Particles(system).Count);
        Assert.AreEqual(1, Particles(system)[0].Age);
    }

    [TestMethod]
    public void ParticlesAreDestroyedOnceTheyOutliveTheirLifetime()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 3, lifeMax: 3);

        ParticlesFunctions.part_particles_create([system, 0, 0, type, 4]);
        Assert.AreEqual(4, Particles(system).Count);

        ParticleManager.UpdateAllSystems();
        ParticleManager.UpdateAllSystems();
        Assert.AreEqual(4, Particles(system).Count, "particles died before their lifetime was up");

        ParticleManager.UpdateAllSystems();
        Assert.AreEqual(0, Particles(system).Count, "particles outlived their lifetime");
    }

    [TestMethod]
    public void ParticlesMoveAtTheirTypesSpeedAndDirection()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 100, lifeMax: 100);

        // Straight up at 2px a step. GML's y axis points down, so "up" is -y.
        ParticlesFunctions.part_type_speed([type, 2, 2, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 90, 90, 0, 0]);

        ParticlesFunctions.part_particles_create([system, 50, 50, type, 1]);
        ParticleManager.UpdateAllSystems();

        var particle = Particles(system)[0];
        Assert.AreEqual(50, particle.X, 0.0001);
        Assert.AreEqual(48, particle.Y, 0.0001);
    }

    [TestMethod]
    public void GravityBendsAParticleBackTheWayItPoints()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 100, lifeMax: 100);

        // Moving right, pulled down: the particle should keep its rightward travel while picking up
        // downward travel, i.e. gravity has to be added as a vector rather than to the raw speed.
        ParticlesFunctions.part_type_speed([type, 1, 1, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 0, 0, 0, 0]);
        ParticlesFunctions.part_type_gravity([type, 0.5, 270]);

        ParticlesFunctions.part_particles_create([system, 0, 0, type, 1]);
        ParticleManager.UpdateAllSystems();

        var particle = Particles(system)[0];
        Assert.AreEqual(1, particle.X, 0.0001);
        Assert.AreEqual(0.5, particle.Y, 0.0001);
    }

    [TestMethod]
    public void AlphaRunsFromStartThroughMiddleToEndOverTheLifetime()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 10, lifeMax: 10);
        ParticlesFunctions.part_type_alpha3([type, 0.0, 1.0, 0.0]);

        ParticlesFunctions.part_particles_create([system, 0, 0, type, 1]);
        var particle = Particles(system)[0];

        Assert.AreEqual(0.0, particle.Alpha, 0.0001, "a new particle starts at alpha1");

        for (var i = 0; i < 5; i++)
        {
            ParticleManager.UpdateAllSystems();
        }

        Assert.AreEqual(1.0, particle.Alpha, 0.0001, "halfway through life it should be at alpha2");

        for (var i = 0; i < 4; i++)
        {
            ParticleManager.UpdateAllSystems();
        }

        Assert.AreEqual(0.2, particle.Alpha, 0.0001, "it should be fading out towards alpha3");
    }

    [TestMethod]
    public void ANegativeBurstCountIsAOneInNChanceOfASingleParticle()
    {
        var system = CreateSystem();
        var type = CreateType();

        var emitter = ParticlesFunctions.part_emitter_create([system]).Conv<int>();
        ParticlesFunctions.part_emitter_region([system, emitter, 0, 0, 0, 0, 0, 0]);

        for (var i = 0; i < 200; i++)
        {
            ParticlesFunctions.part_emitter_burst([system, emitter, type, -4]);
        }

        // Never all-or-nothing: a negative count must roll, not be read as a count of zero (or, via
        // an unsigned conversion somewhere, an enormous one).
        var count = Particles(system).Count;
        Assert.IsTrue(count is > 0 and < 200, $"expected roughly 1 in 4 of 200 bursts to fire, got {count}");
    }

    [TestMethod]
    public void SystemDepthDecidesWhereTheSystemDraws()
    {
        var system = CreateSystem();

        ParticlesFunctions.part_system_depth([system, -100000]);

        Assert.AreEqual(-100000, ParticleManager.PartSystems[system].depth);
    }

    [TestMethod]
    public void DestroyingASystemStopsItDrawingAndFreesItsId()
    {
        var system = CreateSystem();
        var emitter = ParticlesFunctions.part_emitter_create([system]).Conv<int>();

        Assert.IsTrue(Rendering.DrawManager._drawObjects.Contains(ParticleManager.PartSystems[system]));

        ParticlesFunctions.part_system_destroy([system]);

        Assert.IsFalse(ParticlesFunctions.part_system_exists([system]).Conv<bool>());
        Assert.IsFalse(ParticleManager.PartEmitters.ContainsKey(emitter));
        Assert.AreEqual(0, Rendering.DrawManager._drawObjects.Count);
    }

    [TestMethod]
    public void ResetFreesTheIdsAGameRestartExpectsToStartFrom()
    {
        // Games routinely stash a system id in a global and re-run their init after a restart,
        // guarded by `global.ps = 0; if (!part_system_exists(global.ps)) { ...setup... }`. If ids
        // don't restart at 0 with everything else, that guard sees a stale system and silently
        // skips every part_type_create the game was about to do.
        var system = CreateSystem();
        Assert.AreEqual(0, system);

        CreateType();

        ParticleManager.Reset();

        Assert.IsFalse(ParticlesFunctions.part_system_exists([0]).Conv<bool>());
        Assert.AreEqual(0, ParticleManager.PartTypes.Count);
        Assert.AreEqual(0, CreateSystem());
    }

    // part_type_color1/2/3/_rgb/_hsv/_mix used to be entirely unregistered (VMExecutor aborts the
    // whole calling event on an unresolved function), even though
    // ParticleType.ColMode/ColPar and ComputeColor already had full support for every mode wired up
    // underneath. These exercise the GML entry points the same way the burst/lifetime tests above
    // do, to make sure the plumbing was actually connected correctly.

    [TestMethod]
    public void PartTypeColor1SetsAConstantColor()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 4, lifeMax: 4);

        ParticlesFunctions.part_type_color1([type, 0x123456]);
        ParticlesFunctions.part_particles_create([system, 0, 0, type, 1]);

        Assert.AreEqual(0x123456, Particles(system)[0].Color);

        ParticleManager.UpdateAllSystems();
        Assert.AreEqual(0x123456, Particles(system)[0].Color, "color1 should stay constant across the particle's lifetime");
    }

    [TestMethod]
    public void PartTypeColor2InterpolatesAcrossLifetime()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 2, lifeMax: 2);

        ParticlesFunctions.part_type_color2([type, 0x000000, 0xFFFFFF]);
        ParticlesFunctions.part_particles_create([system, 0, 0, type, 1]);

        // At creation (age 0) a two-color particle starts at its first color.
        Assert.AreEqual(0x000000, Particles(system)[0].Color);

        ParticleManager.UpdateAllSystems();

        // Halfway through a 2-step lifetime, the color should be halfway merged. 0x80, not 0x7F:
        // this used to assert the truncated 0x7F7F7F, which was OpenGM's own output rather than
        // GameMaker's -- a capture from GMS 1.4.9999 has merge_color(c_red, c_blue, 0.5) landing on
        // 0x800080, so each channel rounds.
        Assert.AreEqual(0x808080, Particles(system)[0].Color);
    }

    [TestMethod]
    public void PartTypeColorRgbPicksWithinTheGivenChannelRanges()
    {
        var system = CreateSystem();
        var type = CreateType();

        ParticlesFunctions.part_type_color_rgb([type, 10, 20, 30, 40, 50, 60]);
        ParticlesFunctions.part_particles_create([system, 0, 0, type, 20]);

        foreach (var particle in Particles(system))
        {
            var bytes = BitConverter.GetBytes(particle.Color);
            Assert.IsTrue(bytes[0] is >= 10 and <= 20, $"red {bytes[0]} outside range");
            Assert.IsTrue(bytes[1] is >= 30 and <= 40, $"green {bytes[1]} outside range");
            Assert.IsTrue(bytes[2] is >= 50 and <= 60, $"blue {bytes[2]} outside range");
        }
    }

    [TestMethod]
    public void PartTypeColorMixPicksOneOfTheTwoColorsPerParticleAndKeepsItForLife()
    {
        var system = CreateSystem();
        var type = CreateType(lifeMin: 3, lifeMax: 3);

        ParticlesFunctions.part_type_color_mix([type, 0x000000, 0xFFFFFF]);
        ParticlesFunctions.part_particles_create([system, 0, 0, type, 20]);

        var colorsAtBirth = Particles(system).Select(p => p.Color).ToList();

        // A random mix between two colors, chosen once per particle -- not a single constant color.
        Assert.IsTrue(colorsAtBirth.Distinct().Count() > 1);

        ParticleManager.UpdateAllSystems();

        // color_mix isn't handled by the age-based interpolation switch, so it should hold steady.
        CollectionAssert.AreEqual(colorsAtBirth, Particles(system).Select(p => p.Color).ToList());
    }
}
