using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.Particles;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The four region effector families -- attractors, deflectors, destroyers and changers.
///
/// All four were completely absent: 29 functions' worth of <c>part_*_create</c>/<c>_region</c>/
/// <c>_force</c>/... with no data class, no storage and no per-step pass. Because an unresolved call
/// aborts the whole calling event, a game setting up a fountain with a deflector for its basin lost
/// the rest of that Create event too -- so the failure was never "the particles don't bounce", it
/// was "the object never finished initialising".
///
/// These drive the GML entry points and then step the system, so they assert the effect actually
/// reaches the particles rather than just that the setters store a number.
/// </summary>
[TestClass]
public class ParticleEffectorTests
{
    [TestInitialize]
    public void SetUp()
    {
        ParticleManager.Reset();
        GMRandom.InitRandom(12345);
    }

    [TestCleanup]
    public void TearDown() => ParticleManager.Reset();

    private static int CreateSystem() => ParticlesFunctions.part_system_create([]).Conv<int>();

    /// <summary>A type that never moves and lives a long time, so a test only measures the effector.</summary>
    private static int CreateStillType()
    {
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, 1000, 1000]);
        ParticlesFunctions.part_type_speed([type, 0, 0, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 0, 0, 0, 0]);
        return type;
    }

    private static List<Particle> Particles(int system) => ParticleManager.PartSystems[system].Particles;

    private static Particle One(int system, int type, double x, double y)
    {
        ParticlesFunctions.part_particles_create([system, x, y, type, 1]);
        return Particles(system)[^1];
    }

    /*
     * Attractors
     */

    [TestMethod]
    public void AttractorPullsAParticleTowardIt()
    {
        var system = CreateSystem();
        var type = CreateStillType();
        var particle = One(system, type, 0, 0);

        var attractor = ParticlesFunctions.part_attractor_create([system]).Conv<int>();
        ParticlesFunctions.part_attractor_position([system, attractor, 100, 0]);
        // Constant force, in range, non-additive: displaces by exactly `force` per step.
        ParticlesFunctions.part_attractor_force([system, attractor, 10, 200, 0, false]);

        ParticleManager.UpdateSystem(system);

        Assert.AreEqual(10.0, particle.X, 1e-9);
        Assert.AreEqual(0.0, particle.Y, 1e-9);
        Assert.AreEqual(0.0, particle.Speed, 1e-9, "a non-additive attractor must not build momentum");
    }

    [TestMethod]
    public void AttractorIgnoresParticlesOutOfRange()
    {
        var system = CreateSystem();
        var type = CreateStillType();
        var near = One(system, type, 0, 0);
        var far = One(system, type, 500, 0);

        var attractor = ParticlesFunctions.part_attractor_create([system]).Conv<int>();
        ParticlesFunctions.part_attractor_position([system, attractor, 100, 0]);
        ParticlesFunctions.part_attractor_force([system, attractor, 10, 200, 0, false]);

        ParticleManager.UpdateSystem(system);

        Assert.AreEqual(10.0, near.X, 1e-9);
        Assert.AreEqual(500.0, far.X, 1e-9, "400 away from a 200-range attractor must be untouched");
    }

    [TestMethod]
    public void AttractorFalloffKindsDiffer()
    {
        // Same geometry three times: half way into the attractor's range, so linear falloff is
        // exactly half the force and quadratic exactly a quarter.
        double Displacement(int kind)
        {
            ParticleManager.Reset();
            GMRandom.InitRandom(1);
            var system = CreateSystem();
            var type = CreateStillType();
            var particle = One(system, type, 0, 0);

            var attractor = ParticlesFunctions.part_attractor_create([system]).Conv<int>();
            ParticlesFunctions.part_attractor_position([system, attractor, 100, 0]);
            ParticlesFunctions.part_attractor_force([system, attractor, 40, 200, kind, false]);
            ParticleManager.UpdateSystem(system);
            return particle.X;
        }

        Assert.AreEqual(40.0, Displacement(0), 1e-9, "constant");
        Assert.AreEqual(20.0, Displacement(1), 1e-9, "linear");
        Assert.AreEqual(10.0, Displacement(2), 1e-9, "quadratic");
    }

    [TestMethod]
    public void AdditiveAttractorBuildsMomentumInstead()
    {
        var system = CreateSystem();
        var type = CreateStillType();
        var particle = One(system, type, 0, 0);

        var attractor = ParticlesFunctions.part_attractor_create([system]).Conv<int>();
        ParticlesFunctions.part_attractor_position([system, attractor, 100, 0]);
        ParticlesFunctions.part_attractor_force([system, attractor, 5, 200, 0, true]);

        // Effectors run *after* HandleMotion, so a velocity gained this step isn't spent until the
        // next one -- the position always lags the speed by one frame.
        ParticleManager.UpdateSystem(system);
        Assert.AreEqual(5.0, particle.Speed, 1e-9, "additive force goes into the velocity");
        Assert.AreEqual(0.0, particle.X, 1e-9, "and is not also applied to the position this step");

        ParticleManager.UpdateSystem(system);
        var xAfterTwo = particle.X;
        Assert.AreEqual(5.0, xAfterTwo, 1e-9, "now it spends the speed it banked");
        Assert.AreEqual(10.0, particle.Speed, 1e-9);

        // Third step covers twice the ground of the second: that's the acceleration a non-additive
        // attractor would never produce.
        ParticleManager.UpdateSystem(system);
        Assert.AreEqual(10.0, particle.X - xAfterTwo, 1e-9, "an additive attractor accelerates");
    }

    /*
     * Deflectors
     */

    [TestMethod]
    public void DeflectorBouncesAndCostsFriction()
    {
        var system = CreateSystem();
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, 1000, 1000]);
        ParticlesFunctions.part_type_speed([type, 10, 10, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 0, 0, 0, 0]); // straight right

        var particle = One(system, type, 0, 0);

        var deflector = ParticlesFunctions.part_deflector_create([system]).Conv<int>();
        ParticlesFunctions.part_deflector_region([system, deflector, 5, 100, -50, 50]);
        ParticlesFunctions.part_deflector_kind([system, deflector, 0]); // horizontal
        ParticlesFunctions.part_deflector_friction([system, deflector, 2]);

        ParticleManager.UpdateSystem(system);

        // It moved to x=10, which is inside the region, so it gets put back and turned around.
        Assert.AreEqual(0.0, particle.X, 1e-9, "a deflected particle returns to where it started the step");
        Assert.AreEqual(180.0, particle.Dir, 1e-9, "horizontal deflection reverses the x component");
        Assert.AreEqual(8.0, particle.Speed, 1e-9, "friction is taken off the speed");
    }

    [TestMethod]
    public void DeflectorDoesNotTrapParticlesInsideItself()
    {
        // Reverting the position is what stops this: without it the particle stays inside the
        // region and re-deflects every single frame, flipping direction forever instead of leaving.
        var system = CreateSystem();
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, 1000, 1000]);
        ParticlesFunctions.part_type_speed([type, 10, 10, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 0, 0, 0, 0]);

        var particle = One(system, type, 0, 0);

        var deflector = ParticlesFunctions.part_deflector_create([system]).Conv<int>();
        ParticlesFunctions.part_deflector_region([system, deflector, 5, 100, -50, 50]);
        ParticlesFunctions.part_deflector_kind([system, deflector, 0]);

        ParticleManager.UpdateSystem(system); // bounces
        ParticleManager.UpdateSystem(system); // should now be travelling away

        Assert.IsTrue(particle.X < 0, $"particle should have left leftwards, is at {particle.X}");
        Assert.AreEqual(180.0, particle.Dir, 1e-9, "and must not have flipped a second time");
    }

    [TestMethod]
    public void VerticalDeflectionReversesTheOtherAxis()
    {
        var system = CreateSystem();
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, 1000, 1000]);
        ParticlesFunctions.part_type_speed([type, 10, 10, 0, 0]);
        ParticlesFunctions.part_type_direction([type, 90, 90, 0, 0]); // straight up

        var particle = One(system, type, 0, 0);

        var deflector = ParticlesFunctions.part_deflector_create([system]).Conv<int>();
        ParticlesFunctions.part_deflector_region([system, deflector, -50, 50, -100, -5]);
        ParticlesFunctions.part_deflector_kind([system, deflector, 1]); // vertical

        ParticleManager.UpdateSystem(system);

        Assert.AreEqual(270.0, particle.Dir, 1e-9, "up becomes down");
    }

    /*
     * Destroyers
     */

    [TestMethod]
    public void DestroyerRemovesOnlyWhatIsInsideIt()
    {
        var system = CreateSystem();
        var type = CreateStillType();
        One(system, type, 50, 50);   // inside
        One(system, type, 500, 50);  // outside
        Assert.AreEqual(2, Particles(system).Count);

        var destroyer = ParticlesFunctions.part_destroyer_create([system]).Conv<int>();
        ParticlesFunctions.part_destroyer_region([system, destroyer, 0, 100, 0, 100, 0]);

        ParticleManager.UpdateSystem(system);

        Assert.AreEqual(1, Particles(system).Count);
        Assert.AreEqual(500.0, Particles(system)[0].X, 1e-9);
    }

    [TestMethod]
    public void DestroyerRespectsItsRegionShape()
    {
        // A 100x100 ellipse region contains its centre but not its corner; a rectangle contains
        // both. Getting the shape argument wrong is silent -- particles just die in the wrong place.
        bool SurvivesCorner(int shape)
        {
            ParticleManager.Reset();
            GMRandom.InitRandom(1);
            var system = CreateSystem();
            var type = CreateStillType();
            One(system, type, 2, 2); // near the top-left corner of the region
            var destroyer = ParticlesFunctions.part_destroyer_create([system]).Conv<int>();
            ParticlesFunctions.part_destroyer_region([system, destroyer, 0, 100, 0, 100, shape]);
            ParticleManager.UpdateSystem(system);
            return Particles(system).Count == 1;
        }

        Assert.IsFalse(SurvivesCorner(0), "rectangle covers its corner");
        Assert.IsTrue(SurvivesCorner(1), "ellipse does not reach its bounding box's corner");
        Assert.IsTrue(SurvivesCorner(2), "nor does a diamond");
    }

    /*
     * Changers
     */

    [TestMethod]
    public void ChangerRetypesOnlyMatchingParticlesInsideIt()
    {
        var system = CreateSystem();
        var from = CreateStillType();
        var other = CreateStillType();
        var to = CreateStillType();

        var inside = One(system, from, 50, 50);
        var wrongType = One(system, other, 50, 50);
        var outside = One(system, from, 500, 50);

        var changer = ParticlesFunctions.part_changer_create([system]).Conv<int>();
        ParticlesFunctions.part_changer_region([system, changer, 0, 100, 0, 100, 0]);
        ParticlesFunctions.part_changer_types([system, changer, from, to]);
        ParticlesFunctions.part_changer_kind([system, changer, 2]); // ps_change_all

        ParticleManager.UpdateSystem(system);

        // The retyped particle is a replacement object, so look it up by position rather than
        // by holding the old reference.
        var atOrigin = Particles(system).Single(p => Math.Abs(p.X - 50) < 1e-9 && p.PartType == to);
        Assert.AreEqual(to, atOrigin.PartType);
        Assert.AreEqual(other, wrongType.PartType, "a particle of another type is left alone");
        Assert.AreEqual(from, outside.PartType, "a particle outside the region is left alone");
        Assert.AreEqual(3, Particles(system).Count, "changing must not add or drop particles");
        Assert.AreNotSame(inside, atOrigin);
    }

    [TestMethod]
    public void ChangerDoesNothingUntilItsTypesAreSet()
    {
        // A changer defaults to "no types", not to type 0 -- otherwise merely creating one would
        // start retyping whichever particle type happened to be created first.
        var system = CreateSystem();
        var type = CreateStillType();
        var particle = One(system, type, 50, 50);

        var changer = ParticlesFunctions.part_changer_create([system]).Conv<int>();
        ParticlesFunctions.part_changer_region([system, changer, 0, 100, 0, 100, 0]);

        ParticleManager.UpdateSystem(system);

        Assert.AreEqual(type, particle.PartType);
        Assert.AreSame(particle, Particles(system)[0]);
    }

    [TestMethod]
    public void ChangeMotionKeepsAppearanceAndChangeShapeKeepsMotion()
    {
        var system = CreateSystem();

        var from = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([from, 1000, 1000]);
        ParticlesFunctions.part_type_speed([from, 0, 0, 0, 0]);
        ParticlesFunctions.part_type_size([from, 3, 3, 0, 0]);

        var to = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([to, 1000, 1000]);
        ParticlesFunctions.part_type_speed([to, 7, 7, 0, 0]);
        ParticlesFunctions.part_type_size([to, 9, 9, 0, 0]);

        Particle Retype(int kind)
        {
            ParticleManager.Reset();
            GMRandom.InitRandom(1);
            var sys = CreateSystem();

            var a = ParticlesFunctions.part_type_create([]).Conv<int>();
            ParticlesFunctions.part_type_life([a, 1000, 1000]);
            ParticlesFunctions.part_type_speed([a, 0, 0, 0, 0]);
            ParticlesFunctions.part_type_size([a, 3, 3, 0, 0]);

            var b = ParticlesFunctions.part_type_create([]).Conv<int>();
            ParticlesFunctions.part_type_life([b, 1000, 1000]);
            ParticlesFunctions.part_type_speed([b, 7, 7, 0, 0]);
            ParticlesFunctions.part_type_size([b, 9, 9, 0, 0]);

            ParticlesFunctions.part_particles_create([sys, 50, 50, a, 1]);

            var ch = ParticlesFunctions.part_changer_create([sys]).Conv<int>();
            ParticlesFunctions.part_changer_region([sys, ch, 0, 100, 0, 100, 0]);
            ParticlesFunctions.part_changer_types([sys, ch, a, b]);
            ParticlesFunctions.part_changer_kind([sys, ch, kind]);
            ParticleManager.UpdateSystem(sys);

            return ParticleManager.PartSystems[sys].Particles[0];
        }

        // ps_change_motion: takes the new speed, keeps the size it already had.
        var motion = Retype(0);
        Assert.AreEqual(7.0, motion.Speed, 1e-9);
        Assert.AreEqual(3.0, motion.Size, 1e-9);

        // ps_change_shape: the other way round.
        var shape = Retype(1);
        Assert.AreEqual(0.0, shape.Speed, 1e-9);
        Assert.AreEqual(9.0, shape.Size, 1e-9);
    }

    /*
     * Lifecycle, shared by all four
     */

    [TestMethod]
    public void ExistsIsScopedToTheOwningSystem()
    {
        var a = CreateSystem();
        var b = CreateSystem();

        var attractor = ParticlesFunctions.part_attractor_create([a]).Conv<int>();

        Assert.IsTrue(ParticlesFunctions.part_attractor_exists([a, attractor]).Conv<bool>());
        Assert.IsFalse(ParticlesFunctions.part_attractor_exists([b, attractor]).Conv<bool>(),
            "ids are global, so exists has to check ownership too");

        ParticlesFunctions.part_attractor_destroy([a, attractor]);
        Assert.IsFalse(ParticlesFunctions.part_attractor_exists([a, attractor]).Conv<bool>());
    }

    [TestMethod]
    public void DestroyAllAndSystemTeardownForgetEverything()
    {
        var system = CreateSystem();
        var attractor = ParticlesFunctions.part_attractor_create([system]).Conv<int>();
        var deflector = ParticlesFunctions.part_deflector_create([system]).Conv<int>();
        var destroyer = ParticlesFunctions.part_destroyer_create([system]).Conv<int>();
        var changer = ParticlesFunctions.part_changer_create([system]).Conv<int>();

        ParticlesFunctions.part_attractor_destroy_all([system]);
        Assert.IsFalse(ParticlesFunctions.part_attractor_exists([system, attractor]).Conv<bool>());
        Assert.AreEqual(0, ParticleManager.PartAttractors.Count, "destroy_all must free the id, not just unlink it");

        // Tearing the system down takes the other three with it -- otherwise their global entries
        // leak forever, the same way emitters used to.
        ParticlesFunctions.part_system_destroy([system]);
        Assert.AreEqual(0, ParticleManager.PartDeflectors.Count);
        Assert.AreEqual(0, ParticleManager.PartDestroyers.Count);
        Assert.AreEqual(0, ParticleManager.PartChangers.Count);
        _ = (deflector, destroyer, changer);
    }

    [TestMethod]
    public void CreateColorPinsTheColourForTheWholeLife()
    {
        var system = CreateSystem();
        var type = ParticlesFunctions.part_type_create([]).Conv<int>();
        ParticlesFunctions.part_type_life([type, 1000, 1000]);
        ParticlesFunctions.part_type_speed([type, 0, 0, 0, 0]);
        // A colour ramp that would otherwise animate red -> blue across the particle's life.
        ParticlesFunctions.part_type_color2([type, 0x0000FF, 0xFF0000]);

        ParticlesFunctions.part_particles_create_color([system, 0, 0, type, 0x00FF00, 1]);
        var particle = Particles(system)[0];

        Assert.AreEqual(0x00FF00, particle.Color);

        // The point of the flag: without it HandleShape recomputes the colour from the ramp on the
        // very next step and the requested colour is gone after one frame.
        ParticleManager.UpdateSystem(system);
        ParticleManager.UpdateSystem(system);
        Assert.AreEqual(0x00FF00, particle.Color);
    }
}
