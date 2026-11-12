using OpenGM.Particles;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class ParticlesFunctions
    {
        [GMLFunction("part_type_create")]
        public static object? part_type_create(object?[] args)
        {
            return ParticleManager.ParticleTypeCreate();
        }

        [GMLFunction("part_type_destroy")]
        public static object? part_type_destroy(object?[] args)
        {
            var ind = args[0].Conv<int>();

            ParticleManager.PartTypes.Remove(ind);

            return null;
        }

        [GMLFunction("part_type_exists")]
        public static object? part_type_exists(object?[] args)
        {
            var ind = args[0].Conv<int>();
            return ParticleManager.PartTypes.ContainsKey(ind);
        }

        [GMLFunction("part_type_clear")]
        public static object? part_type_clear(object?[] args)
        {
            var ind = args[0].Conv<int>();

            if (ParticleManager.PartTypes.ContainsKey(ind))
            {
                ParticleManager.PartTypes[ind] = new ParticleType();
            }

            return null;
        }

        [GMLFunction("part_type_shape")]
        public static object? part_type_shape(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var shape = args[1].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.Shape = shape;
            // A shape and a sprite are alternatives, not layers: GameMaker drops the sprite here
            // (ParticleType_Shape in the HTML5 runtime does the same), so a type that is given a
            // sprite and then a shape ends up drawing the shape.
            type.Sprite = -1;

            return null;
        }

        [GMLFunction("part_type_sprite")]
        public static object? part_type_sprite(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var sprite = args[1].Conv<int>();
            var animate = args[2].Conv<bool>();
            var stretch = args[3].Conv<bool>();
            var random = args[4].Conv<bool>();

            var type = ParticleManager.PartTypes[ind];
            type.Sprite = sprite;
            type.SpriteAnim = animate;
            type.SpriteStretch = stretch;
            type.SpriteRandom = random;

            return null;
        }

        [GMLFunction("part_type_size")]
        public static object? part_type_size(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var size_min = args[1].Conv<double>();
            var size_max = args[2].Conv<double>();
            var size_incr = args[3].Conv<double>();
            var size_wiggle = args[4].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.SizeMin = size_min;
            type.SizeMax = size_max;
            type.SizeIncr = size_incr;
            type.SizeRandom = size_wiggle;

            return null;
        }

        [GMLFunction("part_type_scale")]
        public static object? part_type_scale(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var xScale = args[1].Conv<double>();
            var yScale = args[2].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.XScale = xScale;
            type.YScale = yScale;

            return null;
        }

        [GMLFunction("part_type_life")]
        public static object? part_type_life(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var life_min = args[1].Conv<int>();
            var life_max = args[2].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.LifeMin = life_min;
            type.LifeMax = life_max;

            return null;
        }

        [GMLFunction("part_type_step")]
        public static object? part_type_step(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var step_number = args[1].Conv<int>();
            var step_type = args[2].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.StepNumber = step_number;
            type.StepType = step_type;

            return null;
        }

        [GMLFunction("part_type_death")]
        public static object? part_type_death(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var death_number = args[1].Conv<int>();
            var death_type = args[2].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.DeathNumber = death_number;
            type.DeathType = death_type;

            return null;
        }

        [GMLFunction("part_type_speed")]
        public static object? part_type_speed(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var speed_min = args[1].Conv<double>();
            var speed_max = args[2].Conv<double>();
            var speed_incr = args[3].Conv<double>();
            var speed_wiggle = args[4].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.SpeedMin = speed_min;
            type.SpeedMax = speed_max;
            type.SpeedIncr = speed_incr;
            type.SpeedRandom = speed_wiggle;

            return null;
        }

        [GMLFunction("part_type_direction")]
        public static object? part_type_direction(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var dir_min = args[1].Conv<double>();
            var dir_max = args[2].Conv<double>();
            var dir_incr = args[3].Conv<double>();
            var dir_wiggle = args[4].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.DirMin = dir_min;
            type.DirMax = dir_max;
            type.DirIncr = dir_incr;
            type.DirRandom = dir_wiggle;
            return null;
        }

        [GMLFunction("part_type_orientation")]
        public static object? part_type_orientation(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var ang_min = args[1].Conv<double>();
            var ang_max = args[2].Conv<double>();
            var ang_incr = args[3].Conv<double>();
            var ang_wiggle = args[4].Conv<double>();
            var ang_relative = args[5].Conv<bool>();

            var type = ParticleManager.PartTypes[ind];
            type.AngleMin = ang_min;
            type.AngleMax = ang_max;
            type.AngleIncr = ang_incr;
            type.AngleRandom = ang_wiggle;
            type.AngleDir = ang_relative;

            return null;
        }

        [GMLFunction("part_type_gravity")]
        public static object? part_type_gravity(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var grav_amount = args[1].Conv<double>();
            var grav_direction = args[2].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.Gravity = grav_amount;
            type.GravityDirection = grav_direction;
            return null;
        }

        [GMLFunction("part_type_color1")]
        [GMLFunction("part_type_colour1")]
        public static object? part_type_color1(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var color1 = args[1].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 0; // COLMODE_ONE
            type.ColPar[0] = color1;

            return null;
        }

        [GMLFunction("part_type_color2")]
        [GMLFunction("part_type_colour2")]
        public static object? part_type_color2(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var color1 = args[1].Conv<int>();
            var color2 = args[2].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 1; // COLMODE_TWO
            type.ColPar[0] = color1;
            type.ColPar[1] = color2;

            return null;
        }

        [GMLFunction("part_type_color3")]
        [GMLFunction("part_type_colour3")]
        [GMLFunction("part_type_color")]
        [GMLFunction("part_type_colour")]
        public static object? part_type_color3(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var color1 = args[1].Conv<int>();
            var color2 = args[2].Conv<int>();
            var color3 = args[3].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 2; // COLMODE_THREE
            type.ColPar[0] = color1;
            type.ColPar[1] = color2;
            type.ColPar[2] = color3;

            return null;
        }

        [GMLFunction("part_type_color_rgb")]
        [GMLFunction("part_type_colour_rgb")]
        public static object? part_type_color_rgb(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var rmin = args[1].Conv<int>();
            var rmax = args[2].Conv<int>();
            var gmin = args[3].Conv<int>();
            var gmax = args[4].Conv<int>();
            var bmin = args[5].Conv<int>();
            var bmax = args[6].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 3; // COLMODE_RGB
            type.ColPar[0] = rmin;
            type.ColPar[1] = rmax;
            type.ColPar[2] = gmin;
            type.ColPar[3] = gmax;
            type.ColPar[4] = bmin;
            type.ColPar[5] = bmax;

            return null;
        }

        [GMLFunction("part_type_color_hsv")]
        [GMLFunction("part_type_colour_hsv")]
        public static object? part_type_color_hsv(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var hmin = args[1].Conv<int>();
            var hmax = args[2].Conv<int>();
            var smin = args[3].Conv<int>();
            var smax = args[4].Conv<int>();
            var vmin = args[5].Conv<int>();
            var vmax = args[6].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 4; // COLMODE_HSV
            type.ColPar[0] = hmin;
            type.ColPar[1] = hmax;
            type.ColPar[2] = smin;
            type.ColPar[3] = smax;
            type.ColPar[4] = vmin;
            type.ColPar[5] = vmax;

            return null;
        }

        [GMLFunction("part_type_color_mix")]
        [GMLFunction("part_type_colour_mix")]
        public static object? part_type_color_mix(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var color1 = args[1].Conv<int>();
            var color2 = args[2].Conv<int>();

            var type = ParticleManager.PartTypes[ind];
            type.ColMode = 5; // COLMODE_MIX
            type.ColPar[0] = color1;
            type.ColPar[1] = color2;

            return null;
        }

        // part_type_alpha is the pre-Studio one-argument spelling of part_type_alpha1, the same way
        // part_type_color aliases part_type_color3. Old code and old tutorials still use it.
        [GMLFunction("part_type_alpha")]
        [GMLFunction("part_type_alpha1")]
        public static object? part_type_alpha1(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var alpha1 = args[1].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.AlphaStart = alpha1;
            type.AlphaMiddle = alpha1;
            type.AlphaEnd = alpha1;

            return null;
        }

        [GMLFunction("part_type_alpha2")]
        public static object? part_type_alpha2(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var alpha1 = args[1].Conv<double>();
            var alpha2 = args[2].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.AlphaStart = alpha1;
            type.AlphaMiddle = (alpha1 + alpha2) / 2;
            type.AlphaEnd = alpha2;

            return null;
        }

        [GMLFunction("part_type_alpha3")]
        public static object? part_type_alpha3(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var alpha1 = args[1].Conv<double>();
            var alpha2 = args[2].Conv<double>();
            var alpha3 = args[3].Conv<double>();

            var type = ParticleManager.PartTypes[ind];
            type.AlphaStart = alpha1;
            type.AlphaMiddle = alpha2;
            type.AlphaEnd = alpha3;

            return null;
        }

        [GMLFunction("part_type_blend")]
        public static object? part_type_blend(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var additive = args[1].Conv<bool>();

            var type = ParticleManager.PartTypes[ind];
            type.AdditiveBlend = additive;

            return null;
        }

        [GMLFunction("part_system_create")]
        public static object? part_system_create(object?[] args)
        {
            return ParticleManager.ParticleSystemCreate();
        }

        [GMLFunction("part_system_destroy")]
        public static object? part_system_destroy(object?[] args)
        {
            var ind = args[0].Conv<int>();

            ParticleManager.ParticleSystemDestroy(ind);

            return null;
        }

        [GMLFunction("part_system_exists")]
        public static object? part_system_exists(object?[] args)
        {
            var ind = args[0].Conv<int>();
            return ParticleManager.PartSystems.ContainsKey(ind);
        }

        [GMLFunction("part_system_clear")]
        public static object? part_system_clear(object?[] args)
        {
            var ind = args[0].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                sys.Particles.Clear();
                ParticleManager.RemoveEmittersOf(sys);
                ParticleManager.RemoveEffectorsOf(sys);
            }

            return null;
        }

        [GMLFunction("part_system_draw_order")]
        public static object? part_system_draw_order(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var oldtonew = args[1].Conv<bool>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                sys.oldtonew = oldtonew;
            }

            return null;
        }

        [GMLFunction("part_system_depth")]
        public static object? part_system_depth(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var depth = args[1].Conv<double>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                // DrawManager re-sorts its list every frame, so this is all it takes to move the
                // whole system in front of or behind the instances around it.
                sys.depth = depth;
            }

            return null;
        }

        [GMLFunction("part_system_position")]
        public static object? part_system_position(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                sys.XDraw = x;
                sys.YDraw = y;
            }

            return null;
        }

        [GMLFunction("part_system_automatic_update")]
        public static object? part_system_automatic_update(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var automatic = args[1].Conv<bool>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                sys.AutomaticUpdate = automatic;
            }

            return null;
        }

        [GMLFunction("part_system_automatic_draw")]
        public static object? part_system_automatic_draw(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var automatic = args[1].Conv<bool>();

            var sys = ParticleManager.PartSystems[ind];
            sys.AutomaticDraw = automatic;

            return null;
        }

        [GMLFunction("part_system_update")]
        public static object? part_system_update(object?[] args)
        {
            var ind = args[0].Conv<int>();

            ParticleManager.UpdateSystem(ind);

            return null;
        }

        [GMLFunction("part_system_drawit")]
        public static object? part_system_drawit(object?[] args)
        {
            var ind = args[0].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                ParticleManager.DrawSystem(sys);
            }

            return null;
        }

        // part_system_create_layer
        // part_system_get_layer
        // part_system_layer

        [GMLFunction("part_particles_create")]
        public static object? part_particles_create(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var parttype = args[3].Conv<int>();
            var number = args[4].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                ParticleManager.ParticleSystem_Particles_Create(sys, x, y, parttype, number);
            }

            return null;
        }

        /// <summary>
        /// Like part_particles_create, but every particle is forced to one colour instead of taking
        /// the type's own colour ramp -- so one type can serve several differently-coloured bursts.
        /// The colour is fixed for the particle's whole life; the ramp is what
        /// <see cref="ParticleManager.HandleShape"/> would otherwise animate.
        /// </summary>
        [GMLFunction("part_particles_create_color")]
        [GMLFunction("part_particles_create_colour")]
        public static object? part_particles_create_color(object?[] args)
        {
            var ind = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var parttype = args[3].Conv<int>();
            var color = args[4].Conv<int>();
            var number = args[5].Conv<int>();

            if (!ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                return null;
            }

            var before = sys.Particles.Count;
            ParticleManager.ParticleSystem_Particles_Create(sys, x, y, parttype, number);

            for (var i = before; i < sys.Particles.Count; i++)
            {
                sys.Particles[i].Color = color;
                sys.Particles[i].FixedColor = true;
            }

            return null;
        }

        [GMLFunction("part_particles_clear")]
        public static object? part_particles_clear(object?[] args)
        {
            var ind = args[0].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ind, out var sys))
            {
                sys.Particles.Clear();
            }

            return null;
        }

        [GMLFunction("part_particles_count")]
        public static object? part_particles_count(object?[] args)
        {
            var ind = args[0].Conv<int>();

            return ParticleManager.PartSystems.TryGetValue(ind, out var sys) ? sys.Particles.Count : 0;
        }

        [GMLFunction("part_emitter_create")]
        public static object? part_emitter_create(object?[] args)
        {
            var ps = args[0].Conv<int>();
            return ParticleManager.ParticleEmitterCreate(ps);
        }

        [GMLFunction("part_emitter_destroy")]
        public static object? part_emitter_destroy(object?[] args)
        {
            var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ps, out var sys) &&
                ParticleManager.PartEmitters.TryGetValue(ind, out var emitter))
            {
                sys.Emitters.Remove(emitter);
            }

            ParticleManager.PartEmitters.Remove(ind);

            return null;
        }

        [GMLFunction("part_emitter_destroy_all")]
        public static object? part_emitter_destroy_all(object?[] args)
        {
            var ps = args[0].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ps, out var sys))
            {
                ParticleManager.RemoveEmittersOf(sys);
            }

            return null;
        }

        [GMLFunction("part_emitter_exists")]
        public static object? part_emitter_exists(object?[] args)
        {
            var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();

            return ParticleManager.PartSystems.TryGetValue(ps, out var sys)
                && ParticleManager.PartEmitters.TryGetValue(ind, out var emitter)
                && sys.Emitters.Contains(emitter);
        }

        [GMLFunction("part_emitter_clear")]
        public static object? part_emitter_clear(object?[] args)
        {
            //var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();

            if (ParticleManager.PartEmitters.TryGetValue(ind, out var emitter))
            {
                emitter.Number = 0;
                emitter.PartType = -1;
                emitter.XMin = 0;
                emitter.XMax = 0;
                emitter.YMin = 0;
                emitter.YMax = 0;
                emitter.Shape = 0;
                emitter.Distribution = 0;
            }

            return null;
        }

        [GMLFunction("part_emitter_region")]
        public static object? part_emitter_region(object?[] args)
        {
            var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();
            var xmin = args[2].Conv<double>();
            var xmax = args[3].Conv<double>();
            var ymin = args[4].Conv<double>();
            var ymax = args[5].Conv<double>();
            var shape = args[6].Conv<int>();
            var distribution = args[7].Conv<int>();

            // currently not used as we store emitters with global ids, instead of per system
            //var sys = ParticleManager.PartSystems[ps]; 
            var emitter = ParticleManager.PartEmitters[ind];

            emitter.XMin = xmin;
            emitter.XMax = xmax;
            emitter.YMin = ymin;
            emitter.YMax = ymax;
            emitter.Shape = shape;
            emitter.Distribution = distribution;

            return null;
        }

        [GMLFunction("part_emitter_burst")]
        public static object? part_emitter_burst(object?[] args)
        {
            var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();
            var parttype = args[2].Conv<int>();
            var number = args[3].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(ps, out var sys) &&
                ParticleManager.PartEmitters.TryGetValue(ind, out var emitter))
            {
                ParticleManager.EmitterBurst(sys, emitter, parttype, number);
            }

            return null;
        }

        [GMLFunction("part_emitter_stream")]
        public static object? part_emitter_stream(object?[] args)
        {
            var ps = args[0].Conv<int>();
            var ind = args[1].Conv<int>();
            var parttype = args[2].Conv<int>();
            var number = args[3].Conv<int>();

            // currently not used as we store emitters with global ids, instead of per system
            //var sys = ParticleManager.PartSystems[ps]; 
            var emitter = ParticleManager.PartEmitters[ind];

            emitter.Number = number;
            emitter.PartType = parttype;

            return null;
        }

        /*
         * Attractors, deflectors, destroyers and changers.
         *
         * All four families follow the same five-function shape (create / destroy / destroy_all /
         * exists / clear) plus their own setters, and all four are addressed the same way emitters
         * already are here: a global id, with the owning system holding a list of what belongs to
         * it. `exists` therefore has to check *both* -- that the id is live and that this system is
         * the one that owns it -- or an id from another system would report as existing here.
         */

        private static bool Owned<T>(object?[] args, Dictionary<int, T> lookup, Func<ParticleSystem, List<T>> owner, out T value)
        {
            value = default!;
            return ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys)
                && lookup.TryGetValue(args[1].Conv<int>(), out value!)
                && owner(sys).Contains(value);
        }

        private static object? Forget<T>(object?[] args, Dictionary<int, T> lookup, Func<ParticleSystem, List<T>> owner)
        {
            var ind = args[1].Conv<int>();

            if (ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys)
                && lookup.TryGetValue(ind, out var value))
            {
                owner(sys).Remove(value);
            }

            lookup.Remove(ind);
            return null;
        }

        // --- attractors ---

        [GMLFunction("part_attractor_create")]
        public static object? part_attractor_create(object?[] args)
            => ParticleManager.ParticleAttractorCreate(args[0].Conv<int>());

        [GMLFunction("part_attractor_destroy")]
        public static object? part_attractor_destroy(object?[] args)
            => Forget(args, ParticleManager.PartAttractors, s => s.Attractors);

        [GMLFunction("part_attractor_destroy_all")]
        public static object? part_attractor_destroy_all(object?[] args)
        {
            if (ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys))
            {
                foreach (var entry in ParticleManager.PartAttractors.Where(x => sys.Attractors.Contains(x.Value)).ToList())
                {
                    ParticleManager.PartAttractors.Remove(entry.Key);
                }
                sys.Attractors.Clear();
            }

            return null;
        }

        [GMLFunction("part_attractor_exists")]
        public static object? part_attractor_exists(object?[] args)
            => Owned(args, ParticleManager.PartAttractors, s => s.Attractors, out _);

        [GMLFunction("part_attractor_clear")]
        public static object? part_attractor_clear(object?[] args)
        {
            if (ParticleManager.PartAttractors.TryGetValue(args[1].Conv<int>(), out var attractor))
            {
                attractor.X = 0;
                attractor.Y = 0;
                attractor.Force = 0;
                attractor.Dist = 0;
                attractor.Kind = 0;
                attractor.Additive = false;
            }

            return null;
        }

        [GMLFunction("part_attractor_position")]
        public static object? part_attractor_position(object?[] args)
        {
            if (ParticleManager.PartAttractors.TryGetValue(args[1].Conv<int>(), out var attractor))
            {
                attractor.X = args[2].Conv<double>();
                attractor.Y = args[3].Conv<double>();
            }

            return null;
        }

        [GMLFunction("part_attractor_force")]
        public static object? part_attractor_force(object?[] args)
        {
            if (ParticleManager.PartAttractors.TryGetValue(args[1].Conv<int>(), out var attractor))
            {
                attractor.Force = args[2].Conv<double>();
                attractor.Dist = args[3].Conv<double>();
                attractor.Kind = args[4].Conv<int>();
                attractor.Additive = args[5].Conv<bool>();
            }

            return null;
        }

        // --- deflectors ---

        [GMLFunction("part_deflector_create")]
        public static object? part_deflector_create(object?[] args)
            => ParticleManager.ParticleDeflectorCreate(args[0].Conv<int>());

        [GMLFunction("part_deflector_destroy")]
        public static object? part_deflector_destroy(object?[] args)
            => Forget(args, ParticleManager.PartDeflectors, s => s.Deflectors);

        [GMLFunction("part_deflector_destroy_all")]
        public static object? part_deflector_destroy_all(object?[] args)
        {
            if (ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys))
            {
                foreach (var entry in ParticleManager.PartDeflectors.Where(x => sys.Deflectors.Contains(x.Value)).ToList())
                {
                    ParticleManager.PartDeflectors.Remove(entry.Key);
                }
                sys.Deflectors.Clear();
            }

            return null;
        }

        [GMLFunction("part_deflector_exists")]
        public static object? part_deflector_exists(object?[] args)
            => Owned(args, ParticleManager.PartDeflectors, s => s.Deflectors, out _);

        [GMLFunction("part_deflector_clear")]
        public static object? part_deflector_clear(object?[] args)
        {
            if (ParticleManager.PartDeflectors.TryGetValue(args[1].Conv<int>(), out var deflector))
            {
                deflector.XMin = 0;
                deflector.XMax = 0;
                deflector.YMin = 0;
                deflector.YMax = 0;
                deflector.Kind = 0;
                deflector.Friction = 0;
            }

            return null;
        }

        [GMLFunction("part_deflector_region")]
        public static object? part_deflector_region(object?[] args)
        {
            if (ParticleManager.PartDeflectors.TryGetValue(args[1].Conv<int>(), out var deflector))
            {
                // Argument order is xmin, xmax, ymin, ymax -- both x bounds before either y bound,
                // the same non-obvious order part_emitter_region uses.
                deflector.XMin = args[2].Conv<double>();
                deflector.XMax = args[3].Conv<double>();
                deflector.YMin = args[4].Conv<double>();
                deflector.YMax = args[5].Conv<double>();
            }

            return null;
        }

        [GMLFunction("part_deflector_kind")]
        public static object? part_deflector_kind(object?[] args)
        {
            if (ParticleManager.PartDeflectors.TryGetValue(args[1].Conv<int>(), out var deflector))
            {
                deflector.Kind = args[2].Conv<int>();
            }

            return null;
        }

        [GMLFunction("part_deflector_friction")]
        public static object? part_deflector_friction(object?[] args)
        {
            if (ParticleManager.PartDeflectors.TryGetValue(args[1].Conv<int>(), out var deflector))
            {
                deflector.Friction = args[2].Conv<double>();
            }

            return null;
        }

        // --- destroyers ---

        [GMLFunction("part_destroyer_create")]
        public static object? part_destroyer_create(object?[] args)
            => ParticleManager.ParticleDestroyerCreate(args[0].Conv<int>());

        [GMLFunction("part_destroyer_destroy")]
        public static object? part_destroyer_destroy(object?[] args)
            => Forget(args, ParticleManager.PartDestroyers, s => s.Destroyers);

        [GMLFunction("part_destroyer_destroy_all")]
        public static object? part_destroyer_destroy_all(object?[] args)
        {
            if (ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys))
            {
                foreach (var entry in ParticleManager.PartDestroyers.Where(x => sys.Destroyers.Contains(x.Value)).ToList())
                {
                    ParticleManager.PartDestroyers.Remove(entry.Key);
                }
                sys.Destroyers.Clear();
            }

            return null;
        }

        [GMLFunction("part_destroyer_exists")]
        public static object? part_destroyer_exists(object?[] args)
            => Owned(args, ParticleManager.PartDestroyers, s => s.Destroyers, out _);

        [GMLFunction("part_destroyer_clear")]
        public static object? part_destroyer_clear(object?[] args)
        {
            if (ParticleManager.PartDestroyers.TryGetValue(args[1].Conv<int>(), out var destroyer))
            {
                destroyer.XMin = 0;
                destroyer.XMax = 0;
                destroyer.YMin = 0;
                destroyer.YMax = 0;
                destroyer.Shape = 0;
            }

            return null;
        }

        [GMLFunction("part_destroyer_region")]
        public static object? part_destroyer_region(object?[] args)
        {
            if (ParticleManager.PartDestroyers.TryGetValue(args[1].Conv<int>(), out var destroyer))
            {
                destroyer.XMin = args[2].Conv<double>();
                destroyer.XMax = args[3].Conv<double>();
                destroyer.YMin = args[4].Conv<double>();
                destroyer.YMax = args[5].Conv<double>();
                destroyer.Shape = args[6].Conv<int>();
            }

            return null;
        }

        // --- changers ---

        [GMLFunction("part_changer_create")]
        public static object? part_changer_create(object?[] args)
            => ParticleManager.ParticleChangerCreate(args[0].Conv<int>());

        [GMLFunction("part_changer_destroy")]
        public static object? part_changer_destroy(object?[] args)
            => Forget(args, ParticleManager.PartChangers, s => s.Changers);

        [GMLFunction("part_changer_destroy_all")]
        public static object? part_changer_destroy_all(object?[] args)
        {
            if (ParticleManager.PartSystems.TryGetValue(args[0].Conv<int>(), out var sys))
            {
                foreach (var entry in ParticleManager.PartChangers.Where(x => sys.Changers.Contains(x.Value)).ToList())
                {
                    ParticleManager.PartChangers.Remove(entry.Key);
                }
                sys.Changers.Clear();
            }

            return null;
        }

        [GMLFunction("part_changer_exists")]
        public static object? part_changer_exists(object?[] args)
            => Owned(args, ParticleManager.PartChangers, s => s.Changers, out _);

        [GMLFunction("part_changer_clear")]
        public static object? part_changer_clear(object?[] args)
        {
            if (ParticleManager.PartChangers.TryGetValue(args[1].Conv<int>(), out var changer))
            {
                changer.XMin = 0;
                changer.XMax = 0;
                changer.YMin = 0;
                changer.YMax = 0;
                changer.Shape = 0;
                // Back to "no types set", not to type 0 -- a cleared changer must do nothing rather
                // than start retyping whatever particle type happens to be first.
                changer.PartTypeFrom = -1;
                changer.PartTypeTo = -1;
                changer.Kind = 2;
            }

            return null;
        }

        [GMLFunction("part_changer_region")]
        public static object? part_changer_region(object?[] args)
        {
            if (ParticleManager.PartChangers.TryGetValue(args[1].Conv<int>(), out var changer))
            {
                changer.XMin = args[2].Conv<double>();
                changer.XMax = args[3].Conv<double>();
                changer.YMin = args[4].Conv<double>();
                changer.YMax = args[5].Conv<double>();
                changer.Shape = args[6].Conv<int>();
            }

            return null;
        }

        [GMLFunction("part_changer_types")]
        public static object? part_changer_types(object?[] args)
        {
            if (ParticleManager.PartChangers.TryGetValue(args[1].Conv<int>(), out var changer))
            {
                changer.PartTypeFrom = args[2].Conv<int>();
                changer.PartTypeTo = args[3].Conv<int>();
            }

            return null;
        }

        [GMLFunction("part_changer_kind")]
        public static object? part_changer_kind(object?[] args)
        {
            if (ParticleManager.PartChangers.TryGetValue(args[1].Conv<int>(), out var changer))
            {
                changer.Kind = args[2].Conv<int>();
            }

            return null;
        }

        // effect_create_blow
        // effect_create_above
        // effect_clear
    }
}
