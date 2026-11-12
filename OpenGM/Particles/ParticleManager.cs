using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine.BuiltInFunctions;
using OpenTK.Mathematics;

namespace OpenGM.Particles
{
    /// <summary>
    /// Ported from the GameMaker HTML5 runtime's yyParticle.js, which is the closest thing to a
    /// readable spec for how particles actually behave:
    /// https://github.com/YoYoGames/GameMaker-HTML5/blob/74b6f0783881bb08be6aab3a73aedb81ed338ca5/scripts/yyParticle.js
    /// </summary>
    public static class ParticleManager
    {
        // Emitter region shapes (part_emitter_region's `shape`).
        private const int ShapeRectangle = 0;
        private const int ShapeEllipse = 1;
        private const int ShapeDiamond = 2;
        private const int ShapeLine = 3;

        // Emitter region distributions (part_emitter_region's `distribution`).
        private const int DistributionLinear = 0;
        private const int DistributionInvGaussian = 2;

        public static int ParticleTypeId;
        public static Dictionary<int, ParticleType> PartTypes = new();

        public static int ParticleSystemId;
        public static Dictionary<int, ParticleSystem> PartSystems = new();

        public static int ParticleEmitterId;
        public static Dictionary<int, ParticleEmitter> PartEmitters = new();

        /// <summary>
        /// Drops every system/type/emitter, as a game restart does. Particle ids have to start over
        /// from zero along with everything else, since games routinely stash the id of the system
        /// they made in a global and then re-run their init script after the restart -- a very
        /// common shape being `global.ps = 0; if (!part_system_exists(global.ps)) { ... }`, which
        /// silently skips all of its particle setup if a stale system is still sitting on id 0.
        /// </summary>
        public static void Reset()
        {
            foreach (var system in PartSystems.Values)
            {
                DrawManager.Unregister(system);
            }

            PartSystems.Clear();
            PartTypes.Clear();
            PartEmitters.Clear();
            PartAttractors.Clear();
            PartDeflectors.Clear();
            PartDestroyers.Clear();
            PartChangers.Clear();

            ParticleSystemId = 0;
            ParticleTypeId = 0;
            ParticleEmitterId = 0;
            ParticleAttractorId = 0;
            ParticleDeflectorId = 0;
            ParticleDestroyerId = 0;
            ParticleChangerId = 0;
        }

        public static int ParticleSystemCreate()
        {
            var element = new CLayerParticleElement
            {
                Type = ElementType.ParticleSystem,
                Id = GameConverter.CurrentElementID++,
            };

            // TODO : add element to room

            var id = ParticleSystemId++;
            var sys = new ParticleSystem();

            element.SystemID = id;
            sys.ElementID = element.Id;
            // Only used to break depth ties against instances, which have their own id space.
            sys.instanceId = element.Id;
            PartSystems.Add(id, sys);
            DrawManager.Register(sys);

            return id;
        }

        public static void ParticleSystemDestroy(int ind)
        {
            if (!PartSystems.Remove(ind, out var sys))
            {
                return;
            }

            RemoveEmittersOf(sys);
            RemoveEffectorsOf(sys);
            sys.Destroy();
        }

        /// <summary>
        /// Drops every emitter belonging to <paramref name="sys"/>, from the system and from the id
        /// lookup both. Emitters are keyed globally rather than per system, so the ones to forget
        /// have to be hunted down by identity.
        /// </summary>
        public static void RemoveEmittersOf(ParticleSystem sys)
        {
            foreach (var emitter in PartEmitters.Where(x => sys.Emitters.Contains(x.Value)).ToList())
            {
                PartEmitters.Remove(emitter.Key);
            }

            sys.Emitters.Clear();
        }

        public static int ParticleTypeCreate()
        {
            var id = ParticleTypeId++;
            PartTypes.Add(id, new ParticleType());
            return id;
        }

        public static int ParticleEmitterCreate(int particleSystem)
        {
            var id = ParticleEmitterId++;
            var sys = PartSystems[particleSystem];

            var emitter = new ParticleEmitter { Created = true };

            sys.Emitters.Add(emitter);
            PartEmitters.Add(id, emitter);

            return id;
        }

        /// <summary>
        /// One step of every system that hasn't been switched to manual with
        /// part_system_automatic_update. Runs once per frame, after the End Step event.
        /// </summary>
        public static void UpdateAllSystems()
        {
            foreach (var sys in PartSystems.Values)
            {
                if (sys.AutomaticUpdate)
                {
                    UpdateSystem(sys);
                }
            }
        }

        public static void UpdateSystem(int ind)
        {
            if (PartSystems.TryGetValue(ind, out var sys))
            {
                UpdateSystem(sys);
            }
        }

        public static void UpdateSystem(ParticleSystem sys)
        {
            HandleLife(sys);
            HandleMotion(sys);

            // The region effectors all act on the positions HandleMotion has just produced, and
            // before HandleShape advances appearance -- so a particle a changer retypes this step
            // gets the new type's colour/size ramp applied on the very same step, not one late.
            // Among themselves the order is: pull, then bounce, then cull, then retype. Destroying
            // ahead of changing means a particle standing in both regions is removed rather than
            // retyped and then removed anyway.
            HandleAttractors(sys);
            HandleDeflectors(sys);
            HandleDestroyers(sys);
            HandleChangers(sys);

            HandleShape(sys);

            foreach (var emitter in sys.Emitters)
            {
                if (emitter.Created && emitter.Number != 0)
                {
                    EmitterBurst(sys, emitter, emitter.PartType, emitter.Number);
                }
            }
        }

        /// <summary>
        /// Ages every particle, spawning each one's step particles as it goes and its death
        /// particles as it expires.
        /// </summary>
        public static void HandleLife(ParticleSystem sys)
        {
            var i = 0;
            while (i < sys.Particles.Count)
            {
                var particle = sys.Particles[i];
                particle.Age++;

                PartTypes.TryGetValue(particle.PartType, out var type);

                if (particle.Age >= particle.Lifetime)
                {
                    if (type != null)
                    {
                        var number = ResolveSpawnCount(type.DeathNumber);
                        if (number > 0)
                        {
                            ParticleSystem_Particles_Create(sys, particle.X, particle.Y, type.DeathType, number);
                        }
                    }

                    sys.Particles.RemoveAt(i);
                    // Don't advance -- removing shuffled the next particle down into this slot.
                    continue;
                }

                if (type != null)
                {
                    var number = ResolveSpawnCount(type.StepNumber);
                    if (number > 0)
                    {
                        ParticleSystem_Particles_Create(sys, particle.X, particle.Y, type.StepType, number);
                    }
                }

                i++;
            }
        }

        public static void HandleMotion(ParticleSystem sys)
        {
            foreach (var particle in sys.Particles)
            {
                if (!PartTypes.TryGetValue(particle.PartType, out var type))
                {
                    // The type was destroyed while particles of it were still in flight.
                    continue;
                }

                particle.Speed += type.SpeedIncr;
                if (particle.Speed < 0)
                {
                    particle.Speed = 0;
                }

                particle.Dir += type.DirIncr;
                particle.Angle += type.AngleIncr;

                if (type.Gravity != 0)
                {
                    // Gravity is a vector added to the particle's own, so it changes direction as
                    // well as speed -- that's what curves a burst back down (or up) over its life.
                    var hspeed = DirectionToVectorH(particle.Dir, particle.Speed)
                        + DirectionToVectorH(type.GravityDirection, type.Gravity);
                    var vspeed = DirectionToVectorV(particle.Dir, particle.Speed)
                        + DirectionToVectorV(type.GravityDirection, type.Gravity);

                    particle.Dir = VectorToDirection(hspeed, vspeed);
                    particle.Speed = Math.Sqrt((hspeed * hspeed) + (vspeed * vspeed));
                }

                // Wiggle only perturbs this step's movement; it never accumulates into the
                // particle's own speed/direction.
                var dir = particle.Dir + (Wiggle(particle, 3, 24, 6) * type.DirRandom);
                var speed = particle.Speed + (Wiggle(particle, 4, 20, 5) * type.SpeedRandom);

                particle.XOld = particle.X;
                particle.YOld = particle.Y;
                particle.X += DirectionToVectorH(dir, speed);
                particle.Y += DirectionToVectorV(dir, speed);
            }
        }

        /// <summary>
        /// Advances everything about a particle's appearance that changes over its life: size,
        /// colour and alpha.
        /// </summary>
        public static void HandleShape(ParticleSystem sys)
        {
            foreach (var particle in sys.Particles)
            {
                if (!PartTypes.TryGetValue(particle.PartType, out var type))
                {
                    continue;
                }

                particle.Size += type.SizeIncr;
                if (particle.Size < 0)
                {
                    particle.Size = 0;
                }

                // part_particles_create_color pins a particle to one colour for its whole life, so
                // the type's ramp must not overwrite it on the next step.
                if (!particle.FixedColor)
                {
                    ComputeColor(particle);
                }

                // Alpha runs start -> middle over the first half of the life and middle -> end over
                // the second, so `passed` is life expressed in halves.
                var passed = particle.Lifetime > 0 ? 2.0 * particle.Age / particle.Lifetime : 1;

                particle.Alpha = passed < 1
                    ? (type.AlphaStart * (1.0 - passed)) + (type.AlphaMiddle * passed)
                    : (type.AlphaMiddle * (2.0 - passed)) + (type.AlphaEnd * (passed - 1));
            }
        }

        public static void DrawSystem(ParticleSystem sys)
        {
            if (sys.Particles.Count == 0)
            {
                return;
            }

            // Blend state is only touched if some particle actually needs additive blending, so a
            // system of ordinary particles leaves the caller's blend mode exactly as it found it.
            var previousBlendmode = _3DFunctions.Blendmode;
            var additiveBlend = false;

            if (sys.oldtonew)
            {
                for (var i = 0; i < sys.Particles.Count; i++)
                {
                    DrawParticle(sys, sys.Particles[i], previousBlendmode, ref additiveBlend);
                }
            }
            else
            {
                for (var i = sys.Particles.Count - 1; i >= 0; i--)
                {
                    DrawParticle(sys, sys.Particles[i], previousBlendmode, ref additiveBlend);
                }
            }

            if (additiveBlend)
            {
                _3DFunctions.gpu_set_blendmode([previousBlendmode]);
            }
        }

        private static void DrawParticle(
            ParticleSystem sys,
            Particle particle,
            int previousBlendmode,
            ref bool additiveBlend)
        {
            if (particle.Lifetime < 1)
            {
                return;
            }

            if (!PartTypes.TryGetValue(particle.PartType, out var type))
            {
                return;
            }

            // A particle type is drawn with its sprite if it has a usable one, and with the built-in
            // texture for its part_type_shape otherwise -- including when it was never given either,
            // since a fresh type starts on pt_shape_pixel rather than on nothing.
            var data = SpriteManager.SpriteExists(type.Sprite)
                ? SpriteManager.GetSpriteAsset(type.Sprite)
                : null;

            if (data is { Textures.Count: < 1 })
            {
                // An empty sprite counts as no sprite, so this falls back to the shape too.
                data = null;
            }

            // Orientation and size wiggle are applied for this frame only, and aren't stored back on
            // the particle -- unlike the increments, which are.
            var angle = particle.Angle + (Wiggle(particle, 2, 16, 4) * type.AngleRandom);
            if (type.AngleDir)
            {
                angle += particle.Dir;
            }

            var size = particle.Size + (Wiggle(particle, 1, 16, 4) * type.SizeRandom);

            // Non-additive particles are drawn under whatever blend mode was already set rather than
            // being forced to bm_normal, so the mode only ever moves to bm_add and back again.
            if (additiveBlend != type.AdditiveBlend)
            {
                additiveBlend = type.AdditiveBlend;
                _3DFunctions.gpu_set_blendmode([additiveBlend ? 1 : previousBlendmode]);
            }

            if (data == null)
            {
                DrawShapeParticle(sys, particle, type, angle, size);
                return;
            }

            double subimage;
            if (!type.SpriteAnim)
            {
                subimage = particle.SpriteStart;
            }
            else if (type.SpriteStretch)
            {
                // Stretch fits exactly one pass of the animation into the particle's lifetime.
                subimage = particle.SpriteStart
                    + ((double)data.Textures.Count * particle.Age / Math.Max(particle.Lifetime, 1));
            }
            else
            {
                subimage = particle.SpriteStart + particle.Age;
            }

            SpriteManager.DrawSpriteExt(
                type.Sprite,
                subimage,
                particle.X + sys.XDraw,
                particle.Y + sys.YDraw,
                type.XScale * size,
                type.YScale * size,
                angle,
                particle.Color,
                particle.Alpha);
        }

        /// <summary>
        /// Draws one of part_type_shape's built-in textures. These never animate and aren't sprite
        /// assets at all (see <see cref="ParticleShapes"/>), so they go straight to a draw job
        /// rather than through SpriteManager: each is a 64x64 image drawn from its centre, which is
        /// what makes a size-1 pt_shape_pixel the few-pixel dot it looks like in GameMaker rather
        /// than a 64-pixel block.
        /// </summary>
        private static void DrawShapeParticle(
            ParticleSystem sys,
            Particle particle,
            ParticleType type,
            double angle,
            double size)
        {
            var texture = ParticleShapes.GetTexture(type.Shape);
            if (texture == null)
            {
                return;
            }

            var colour = particle.Color.ABGRToCol4(particle.Alpha);

            CustomWindow.Draw(new GMSpriteJob
            {
                texture = texture,
                screenPos = new Vector2d(particle.X + sys.XDraw, particle.Y + sys.YDraw),
                angle = angle,
                scale = new Vector2d(type.XScale * size, type.YScale * size),
                Colors = [colour, colour, colour, colour],
                origin = new Vector2(texture.SourceWidth / 2f, texture.SourceHeight / 2f)
            });
        }

        // ParticleSystem_Emitter_Burst
        public static void EmitterBurst(ParticleSystem system, ParticleEmitter emitter, int parttype, int number)
        {
            number = ResolveSpawnCount(number);
            if (number <= 0)
            {
                return;
            }

            var width = emitter.XMax - emitter.XMin;
            var height = emitter.YMax - emitter.YMin;

            for (var i = 0; i < number; i++)
            {
                // Positions are picked in unit-square space and then mapped onto the region, so the
                // rejection sampling the non-rectangular shapes need is independent of its size.
                double xx, yy;
                while (true)
                {
                    xx = MyRandom(0, 1, emitter.Distribution);
                    yy = MyRandom(0, 1, emitter.Distribution);

                    if (emitter.Distribution == DistributionInvGaussian && emitter.Shape != ShapeLine)
                    {
                        // An inverse gaussian on both axes at once would pile everything into the
                        // corners, so one axis is redrawn flat.
                        if (GMRandom.fYYRandom() < 0.5)
                        {
                            xx = MyRandom(0, 1, DistributionLinear);
                        }
                        else
                        {
                            yy = MyRandom(0, 1, DistributionLinear);
                        }
                    }

                    if (emitter.Shape == ShapeEllipse)
                    {
                        var dx = xx - 0.5;
                        var dy = yy - 0.5;
                        if ((dx * dx) + (dy * dy) <= 0.25)
                        {
                            break;
                        }
                    }
                    else if (emitter.Shape == ShapeDiamond)
                    {
                        if (Math.Abs(xx - 0.5) + Math.Abs(yy - 0.5) <= 0.5)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // ShapeRectangle / ShapeLine take the sample as-is.
                        break;
                    }
                }

                // A line region walks both axes with the same parameter, tracing the diagonal of
                // the region rather than filling it.
                var x = emitter.XMin + (width * xx);
                var y = emitter.YMin + (height * (emitter.Shape == ShapeLine ? xx : yy));

                ParticleSystem_Particles_Create(system, x, y, parttype, 1);
            }
        }

        public static Particle? CreateParticle(double x, double y, int particleTypeId)
        {
            if (!PartTypes.TryGetValue(particleTypeId, out var type))
            {
                return null;
            }

            var p = new Particle();

            p.PartType = particleTypeId;
            p.X = x;
            p.Y = y;
            p.XOld = x;
            p.YOld = y;
            p.Speed = MyRandom(type.SpeedMin, type.SpeedMax, 0);
            p.Dir = MyRandom(type.DirMin, type.DirMax, 0);
            p.Angle = MyRandom(type.AngleMin, type.AngleMax, 0);
            p.Lifetime = (int)MyRandom(type.LifeMin, type.LifeMax, 0); // TODO : exactly how is this rounded?
            p.Age = 0;
            ComputeColor(p);
            p.Alpha = type.AlphaStart;
            p.Size = MyRandom(type.SizeMin, type.SizeMax, 0);
            if (!type.SpriteRandom)
            {
                p.SpriteStart = 0;
            }
            else
            {
                p.SpriteStart = (int)GMRandom.YYRandom(10000);
            }
            p.Ran = (int)GMRandom.YYRandom(10000);

            return p;
        }

        public static void ParticleSystem_Particles_Create(
            ParticleSystem system,
            double x,
            double y,
            int parttype,
            int numb)
        {
            for (var i = 0; i < numb; i++)
            {
                var particle = CreateParticle(x, y, parttype);
                if (particle == null)
                {
                    return;
                }

                system.Particles.Add(particle);
            }
        }

        /*
         * Region effectors -- attractors, deflectors, destroyers and changers.
         *
         * All four are created per particle system (part_*_create(ps)) but, like emitters here, are
         * keyed in one global id dictionary with the system holding a list of the ones that belong
         * to it. Behaviour follows the HTML5 runtime's yyParticle.js.
         */

        public static int ParticleAttractorId;
        public static Dictionary<int, ParticleAttractor> PartAttractors = new();

        public static int ParticleDeflectorId;
        public static Dictionary<int, ParticleDeflector> PartDeflectors = new();

        public static int ParticleDestroyerId;
        public static Dictionary<int, ParticleDestroyer> PartDestroyers = new();

        public static int ParticleChangerId;
        public static Dictionary<int, ParticleChanger> PartChangers = new();

        // Force falloff kinds (part_attractor_force's `kind`).
        private const int ForceConstant = 0;
        private const int ForceLinear = 1;
        private const int ForceQuadratic = 2;

        // Deflection kinds (part_deflector_kind).
        private const int DeflectHorizontal = 0;

        // Change kinds (part_changer_kind).
        private const int ChangeMotion = 0;
        private const int ChangeShape = 1;

        /// <summary>
        /// Whether a point falls inside a region of the given shape. Shared by destroyers and
        /// changers, and the same shape numbering emitters use. A line region has no interior, so it
        /// is treated as its bounding rectangle -- otherwise a destroyer set to ps_shape_line would
        /// silently never fire.
        /// </summary>
        private static bool InRegion(double x, double y, double xmin, double ymin, double xmax, double ymax, int shape)
        {
            if (xmin > xmax)
                (xmin, xmax) = (xmax, xmin);
            if (ymin > ymax)
                (ymin, ymax) = (ymax, ymin);

            if (x < xmin || x > xmax || y < ymin || y > ymax)
            {
                return false;
            }

            var width = xmax - xmin;
            var height = ymax - ymin;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            // Normalised to the unit square, so ellipse and diamond are the same tests the emitter
            // sampler uses.
            var u = (x - xmin) / width;
            var v = (y - ymin) / height;

            return shape switch
            {
                ShapeEllipse => ((u - 0.5) * (u - 0.5)) + ((v - 0.5) * (v - 0.5)) <= 0.25,
                ShapeDiamond => Math.Abs(u - 0.5) + Math.Abs(v - 0.5) <= 0.5,
                _ => true,
            };
        }

        public static void HandleAttractors(ParticleSystem sys)
        {
            foreach (var attractor in sys.Attractors)
            {
                if (attractor.Force == 0 || attractor.Dist <= 0)
                {
                    continue;
                }

                foreach (var particle in sys.Particles)
                {
                    var dx = attractor.X - particle.X;
                    var dy = attractor.Y - particle.Y;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));

                    // Sitting exactly on the attractor has no direction to be pulled in, and would
                    // divide by zero working one out.
                    if (distance == 0 || distance > attractor.Dist)
                    {
                        continue;
                    }

                    var falloff = 1 - (distance / attractor.Dist);
                    var force = attractor.Kind switch
                    {
                        ForceLinear => attractor.Force * falloff,
                        ForceQuadratic => attractor.Force * falloff * falloff,
                        _ => attractor.Force, // ForceConstant
                    };

                    var ux = dx / distance;
                    var uy = dy / distance;

                    if (attractor.Additive)
                    {
                        // Fold the pull into the velocity, so it accumulates and the particle can
                        // overshoot and orbit.
                        var hspeed = DirectionToVectorH(particle.Dir, particle.Speed) + (force * ux);
                        var vspeed = DirectionToVectorV(particle.Dir, particle.Speed) + (force * uy);

                        particle.Dir = VectorToDirection(hspeed, vspeed);
                        particle.Speed = Math.Sqrt((hspeed * hspeed) + (vspeed * vspeed));
                    }
                    else
                    {
                        particle.X += force * ux;
                        particle.Y += force * uy;
                    }
                }
            }
        }

        public static void HandleDeflectors(ParticleSystem sys)
        {
            foreach (var deflector in sys.Deflectors)
            {
                foreach (var particle in sys.Particles)
                {
                    // A deflector is a plain rectangle -- no shape kinds, unlike destroyers.
                    if (!InRegion(particle.X, particle.Y, deflector.XMin, deflector.YMin,
                            deflector.XMax, deflector.YMax, ShapeRectangle))
                    {
                        continue;
                    }

                    // Put the particle back where it started the step before turning it around,
                    // otherwise it stays inside the region and re-deflects every frame, jittering in
                    // place instead of bouncing off.
                    particle.X = particle.XOld;
                    particle.Y = particle.YOld;

                    particle.Dir = deflector.Kind == DeflectHorizontal
                        ? 180 - particle.Dir   // reverse the horizontal component
                        : 360 - particle.Dir;  // reverse the vertical component

                    particle.Speed -= deflector.Friction;
                    if (particle.Speed < 0)
                    {
                        particle.Speed = 0;
                    }
                }
            }
        }

        public static void HandleDestroyers(ParticleSystem sys)
        {
            if (sys.Destroyers.Count == 0)
            {
                return;
            }

            sys.Particles.RemoveAll(particle => sys.Destroyers.Any(destroyer =>
                InRegion(particle.X, particle.Y, destroyer.XMin, destroyer.YMin,
                    destroyer.XMax, destroyer.YMax, destroyer.Shape)));
        }

        public static void HandleChangers(ParticleSystem sys)
        {
            foreach (var changer in sys.Changers)
            {
                if (changer.PartTypeFrom < 0 || changer.PartTypeTo < 0)
                {
                    continue;
                }

                for (var i = 0; i < sys.Particles.Count; i++)
                {
                    var particle = sys.Particles[i];

                    if (particle.PartType != changer.PartTypeFrom
                        || !InRegion(particle.X, particle.Y, changer.XMin, changer.YMin,
                            changer.XMax, changer.YMax, changer.Shape))
                    {
                        continue;
                    }

                    sys.Particles[i] = ChangeParticle(particle, changer.PartTypeTo, changer.Kind);
                }
            }
        }

        /// <summary>
        /// Retypes one particle. The type always changes -- a particle stores only instantaneous
        /// values and reads every per-step increment off its type, so there is no such thing as
        /// keeping half of the old type. What <paramref name="kind"/> selects is which of the
        /// particle's *current* attributes get re-rolled from the new type: ps_change_motion
        /// re-rolls speed and direction, ps_change_shape re-rolls size/angle/colour/alpha, and
        /// ps_change_all replaces the particle outright (a fresh lifetime and age included, so it
        /// starts the new type's colour ramp from the beginning).
        /// </summary>
        private static Particle ChangeParticle(Particle particle, int newType, int kind)
        {
            var replacement = CreateParticle(particle.X, particle.Y, newType);
            if (replacement == null)
            {
                // The target type was destroyed; leave the particle exactly as it was.
                return particle;
            }

            replacement.XOld = particle.XOld;
            replacement.YOld = particle.YOld;

            if (kind == ChangeMotion)
            {
                // Keep how it looks and how far through its life it is; take the new motion.
                replacement.Age = particle.Age;
                replacement.Lifetime = particle.Lifetime;
                replacement.Angle = particle.Angle;
                replacement.Color = particle.Color;
                replacement.Alpha = particle.Alpha;
                replacement.Size = particle.Size;
            }
            else if (kind == ChangeShape)
            {
                // Keep where it's going; take the new appearance.
                replacement.Age = particle.Age;
                replacement.Lifetime = particle.Lifetime;
                replacement.Speed = particle.Speed;
                replacement.Dir = particle.Dir;
            }

            return replacement;
        }

        public static int ParticleAttractorCreate(int ps)
        {
            var attractor = new ParticleAttractor();
            var id = ParticleAttractorId++;
            PartAttractors.Add(id, attractor);

            if (PartSystems.TryGetValue(ps, out var sys))
            {
                sys.Attractors.Add(attractor);
            }

            return id;
        }

        public static int ParticleDeflectorCreate(int ps)
        {
            var deflector = new ParticleDeflector();
            var id = ParticleDeflectorId++;
            PartDeflectors.Add(id, deflector);

            if (PartSystems.TryGetValue(ps, out var sys))
            {
                sys.Deflectors.Add(deflector);
            }

            return id;
        }

        public static int ParticleDestroyerCreate(int ps)
        {
            var destroyer = new ParticleDestroyer();
            var id = ParticleDestroyerId++;
            PartDestroyers.Add(id, destroyer);

            if (PartSystems.TryGetValue(ps, out var sys))
            {
                sys.Destroyers.Add(destroyer);
            }

            return id;
        }

        public static int ParticleChangerCreate(int ps)
        {
            var changer = new ParticleChanger();
            var id = ParticleChangerId++;
            PartChangers.Add(id, changer);

            if (PartSystems.TryGetValue(ps, out var sys))
            {
                sys.Changers.Add(changer);
            }

            return id;
        }

        /// <summary>
        /// Drops every attractor/deflector/destroyer/changer belonging to <paramref name="sys"/>,
        /// from the system's own lists and from the global id lookups both -- the same cleanup
        /// <see cref="RemoveEmittersOf"/> does, and for the same reason: leaving the id entries
        /// behind would keep them alive forever and make part_*_exists lie.
        /// </summary>
        public static void RemoveEffectorsOf(ParticleSystem sys)
        {
            RemoveAllFrom(PartAttractors, sys.Attractors);
            RemoveAllFrom(PartDeflectors, sys.Deflectors);
            RemoveAllFrom(PartDestroyers, sys.Destroyers);
            RemoveAllFrom(PartChangers, sys.Changers);
        }

        private static void RemoveAllFrom<T>(Dictionary<int, T> lookup, List<T> owned)
        {
            foreach (var entry in lookup.Where(x => owned.Contains(x.Value)).ToList())
            {
                lookup.Remove(entry.Key);
            }

            owned.Clear();
        }

        /// <summary>
        /// Resolves a particle count the way every "number of particles" argument in the particle
        /// API is read: a negative count means a 1-in-N chance of a single particle instead.
        /// </summary>
        private static int ResolveSpawnCount(int number)
        {
            if (number >= 0)
            {
                return number;
            }

            return GMRandom.YYRandom(-number) == 0 ? 1 : 0;
        }

        /// <summary>
        /// The triangle wave in [-1, 1] that drives every "wiggle" parameter. It's a function of the
        /// particle's age rather than a fresh random number, so a wiggling particle oscillates
        /// smoothly instead of juddering; <see cref="Particle.Ran"/> shifts each particle to its own
        /// phase, and the varying multipliers/periods keep the speed, direction, orientation and
        /// size waves from lining up with each other.
        /// </summary>
        private static double Wiggle(Particle particle, int ranScale, int period, double quarter)
        {
            var value = ((particle.Age + (ranScale * particle.Ran)) % period) / quarter;

            if (value > 2.0)
            {
                value = 4.0 - value;
            }

            return value - 1.0;
        }

        private static double DirectionToVectorH(double dir, double speed)
            => speed * Math.Cos(dir * CustomMath.Deg2Rad);

        private static double DirectionToVectorV(double dir, double speed)
            => -speed * Math.Sin(dir * CustomMath.Deg2Rad);

        private static double VectorToDirection(double h, double v)
        {
            double dir;

            if (h == 0)
            {
                dir = v > 0 ? 270 : v < 0 ? 90 : 0;
            }
            else
            {
                var degrees = 180.0 * Math.Atan2(v, h) / Math.PI;
                dir = degrees <= 0 ? -degrees : 360.0 - degrees;
            }

            return dir - (360.0 * Math.Floor(dir / 360.0));
        }

        // https://github.com/YoYoGames/GameMaker-HTML5/blob/74b6f0783881bb08be6aab3a73aedb81ed338ca5/scripts/yyParticle.js#L626
        private static void ComputeColor(Particle p)
        {
            var type = PartTypes[p.PartType];

            if (p.Age <= 0 || p.Lifetime <= 0)
            {
                switch (type.ColMode)
                {
                    case 0: // COLMODE_ONE
                    case 1: // COLMODE_TWO
                    case 2: // COLMODE_THREE
                        p.Color = type.ColPar[0];
                        break;
                    case 3: // COLMODE_RGB
                        var r = (int)MyRandom(type.ColPar[0], type.ColPar[1], 0);
                        var g = (int)MyRandom(type.ColPar[2], type.ColPar[3], 0);
                        var b = (int)MyRandom(type.ColPar[4], type.ColPar[5], 0);
                        p.Color = (int)GraphicFunctions.make_color_rgb(r, g, b);
                        break;
                    case 4: // COLMODE_HSV
                        var h = (int)MyRandom(type.ColPar[0], type.ColPar[1], 0);
                        var s = (int)MyRandom(type.ColPar[2], type.ColPar[3], 0);
                        var v = (int)MyRandom(type.ColPar[4], type.ColPar[5], 0);
                        p.Color = (int)GraphicFunctions.make_color_hsv(h, s, v);
                        break;
                    case 5: // COLMODE_MIX
                        p.Color = (int)GraphicFunctions.merge_colour(type.ColPar[0], type.ColPar[1], GMRandom.fYYRandom());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                switch (type.ColMode)
                {
                    case 0: // COLMODE_ONE
                        p.Color = type.ColPar[0];
                        break;
                    case 1: // COLMODE_TWO
                        {
                            var val = p.Age / (double)p.Lifetime;
                            if (val > 1)
                            {
                                val = 1;
                            }

                            p.Color = (int)GraphicFunctions.merge_colour(type.ColPar[0], type.ColPar[1], val);
                            break;
                        }
                    case 2: // COLMODE_THREE
                        {
                            var val = 2 * p.Age / (double)p.Lifetime;
                            if (val > 2)
                            {
                                val = 2;
                            }

                            if (val < 1)
                            {
                                p.Color = (int)GraphicFunctions.merge_colour(type.ColPar[0], type.ColPar[1], val);
                            }
                            else
                            {
                                p.Color = (int)GraphicFunctions.merge_colour(type.ColPar[1], type.ColPar[2], val - 1);
                            }

                            break;
                        }
                }
            }
        }

        private static double MyRandom(double min, double max, double dist)
        {
            var range = max - min;

            if (range <= 0)
            {
                return min;
            }

            double xx;

            switch (dist)
            {
                case 0: // PART_EDISTR_LINEAR
                    return (GMRandom.fYYRandom() * range) + min;
                case 1: // PART_EDISTR_GAUSSIAN
                    do
                    {
                        xx = (GMRandom.fYYRandom() - 0.5) * 6;
                    } while (Math.Exp(-(xx * xx) * 0.5) <= GMRandom.fYYRandom());

                    return min + ((xx + 3) * (1.0 / 6.0)) * range;
                case 2: // PART_EDISTR_INVGAUSSIAN
                    do
                    {
                        xx = (GMRandom.fYYRandom() - 0.5) * 6;
                    } while (!(Math.Exp(-(xx * xx) * 0.5) > GMRandom.fYYRandom()));

                    if (xx < 0)
                    {
                        xx += 6;
                    }

                    return min + (xx * (1.0 / 6.0)) * range;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
