using OpenGM.Rendering;
using OpenGM.SerializedFiles;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class ActionFunctions
    {
        // todo: order these properly

        public static bool Action_Relative = false;

        private static (double x, double y) RelativeOffset(double x, double y)
        {
            if (!Action_Relative)
            {
                return (x, y);
            }

            var self = VMExecutor.Self.GMSelf;
            return (x + self.x, y + self.y);
        }

        [GMLFunction("action_change_object")]
        public static object? action_change_object(object?[] args) => GameFunctions.instance_change(args);

        private static int CreateObjectInstance(int obj, double x, double y, double? speed = null, double? direction = null)
        {
            var definition = InstanceManager.ObjectDefinitions[obj];

            var newGM = new GamemakerObject(definition, x, y, definition.depth, InstanceManager.NextInstanceID++,
                definition.sprite, definition.visible, definition.persistent, definition.textureMaskId);

            GamemakerObject.ExecuteEvent(newGM, definition, EventType.PreCreate);

            // Real GM8 sets the instance's motion before its Create event runs, so Create can read
            // the final speed/direction/hspeed/vspeed if it wants to.
            if (direction != null)
            {
                newGM.direction = direction.Value;
            }
            if (speed != null)
            {
                newGM.speed = speed.Value;
            }

            GamemakerObject.ExecuteEvent(newGM, definition, EventType.Create);
            newGM._createRan = true;
            return newGM.instanceId;
        }

        [GMLFunction("action_create_object")]
        public static object? action_create_object(object?[] args)
        {
            var obj = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();

            return CreateObjectInstance(obj, x, y);
        }

        [GMLFunction("action_create_object_motion")]
        public static object? action_create_object_motion(object?[] args)
        {
            var obj = args[0].Conv<int>();
            var x = args[1].Conv<double>();
            var y = args[2].Conv<double>();
            var speed = args[3].Conv<double>();
            var direction = args[4].Conv<double>();

            return CreateObjectInstance(obj, x, y, speed, direction);
        }

        [GMLFunction("action_end_sound")]
        public static object? action_end_sound(object?[] args) => SoundFunctionsLegacy.sound_stop(args);

        [GMLFunction("action_if")]
        public static object? action_if(object?[] args) => args[0];

        [GMLFunction("action_move_point")]
        public static object? action_move_point(object?[] args) => GameFunctions.move_towards_point(args);

        [GMLFunction("action_move_random")]
        public static object? action_move_random(object?[] args) => GameFunctions.move_random(args);

        [GMLFunction("action_reverse_xdir")]
        public static object? action_reverse_xdir(object?[] args)
        {
            var self = VMExecutor.Self.GMSelf;
            self.hspeed = -self.hspeed;
            return null;
        }

        [GMLFunction("action_reverse_ydir")]
        public static object? action_reverse_ydir(object?[] args)
        {
            var self = VMExecutor.Self.GMSelf;
            self.vspeed = -self.vspeed;
            return null;
        }

        [GMLFunction("action_set_motion")]
        public static object? action_set_motion(object?[] args)
        {
            var direction = args[0].Conv<double>();
            var speed = args[1].Conv<double>();

            var self = VMExecutor.Self.GMSelf;
            self.direction = direction;
            self.speed = speed;
            return null;
        }

        [GMLFunction("action_sound")]
        public static object? action_sound(object?[] args)
        {
            var sound = args[0];
            var doLoop = args[1].Conv<bool>();

            return doLoop
                ? SoundFunctionsLegacy.sound_loop(new[] { sound })
                : SoundFunctionsLegacy.sound_play(new[] { sound });
        }

        [GMLFunction("action_sprite_set")]
        public static object? action_sprite_set(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var imageIndex = args[1].Conv<double>();
            var imageSpeed = args[2].Conv<double>();

            var self = VMExecutor.Self.GMSelf;
            self.sprite_index = sprite;

            if (imageIndex >= 0)
            {
                self.image_index = imageIndex;
            }

            self.image_speed = imageSpeed;
            return null;
        }

        [GMLFunction("action_sprite_transform")]
        public static object? action_sprite_transform(object?[] args)
        {
            var xScale = args[0].Conv<double>();
            var yScale = args[1].Conv<double>();
            var angle = args[2].Conv<double>();
            var mirroring = args[3].Conv<int>();

            // 0 = none, 1 = horizontal, 2 = vertical, 3 = both
            var hMirror = mirroring is 1 or 3;
            var vMirror = mirroring is 2 or 3;

            var self = VMExecutor.Self.GMSelf;
            self.image_xscale = hMirror ? -xScale : xScale;
            self.image_yscale = vMirror ? -yScale : yScale;
            self.image_angle = angle;
            return null;
        }

        [GMLFunction("action_bounce")]
        public static object? action_bounce(object?[] args)
        {
            var advanced = args[0];
            var kind = args[1].Conv<int>();

            return kind == 0
                ? GameFunctions.move_bounce_solid(new[] { advanced })
                : GameFunctions.move_bounce_all(new[] { advanced });
        }

        // The DnD wrappers around an instance's timeline state. Semantics (notably which argument
        // means "start" vs "loop", and that start_option is inverted) follow OpenGMK's kernel.rs.
        [GMLFunction("action_timeline_set")]
        public static object? action_timeline_set(object?[] args)
        {
            var index = args[0].Conv<int>();
            var position = args[1].Conv<double>();
            var startOption = args[2].Conv<int>();
            var loopOption = args[3].Conv<int>();

            var self = VMExecutor.Self.GMSelf;
            self.timeline_index = index;
            self.timeline_position = position;
            // 0 is "start immediately" in the DnD dialog, so it's the *running* case.
            self.timeline_running = startOption == 0;
            self.timeline_loop = loopOption == 1;
            return null;
        }

        [GMLFunction("action_set_timeline")]
        public static object? action_set_timeline(object?[] args)
        {
            var index = args[0].Conv<int>();
            var position = args[1].Conv<double>();

            var self = VMExecutor.Self.GMSelf;
            self.timeline_index = index;
            self.timeline_position = position;
            self.timeline_running = true;
            return null;
        }

        [GMLFunction("action_timeline_start")]
        public static object? action_timeline_start(object?[] args)
        {
            VMExecutor.Self.GMSelf.timeline_running = true;
            return null;
        }

        [GMLFunction("action_timeline_pause")]
        public static object? action_timeline_pause(object?[] args)
        {
            VMExecutor.Self.GMSelf.timeline_running = false;
            return null;
        }

        [GMLFunction("action_timeline_stop")]
        public static object? action_timeline_stop(object?[] args)
        {
            var self = VMExecutor.Self.GMSelf;
            // Stop rewinds, pause doesn't -- that's the whole difference between the two.
            self.timeline_position = 0;
            self.timeline_running = false;
            return null;
        }

        [GMLFunction("action_set_timeline_position")]
        public static object? action_set_timeline_position(object?[] args)
        {
            var position = args[0].Conv<double>();

            var self = VMExecutor.Self.GMSelf;
            self.timeline_position = Action_Relative ? self.timeline_position + position : position;
            return null;
        }

        [GMLFunction("action_set_timeline_speed")]
        public static object? action_set_timeline_speed(object?[] args)
        {
            var speed = args[0].Conv<double>();

            var self = VMExecutor.Self.GMSelf;
            self.timeline_speed = Action_Relative ? self.timeline_speed + speed : speed;
            return null;
        }

        [GMLFunction("action_kill_object", before: "2.0.0.0")]
        public static object? action_kill_object(object?[] args)
        {
            return GameFunctions.instance_destroy(args);
        }

        [GMLFunction("action_move", before: "2.0.0.0")]
        public static object? action_move(object?[] args)
        {
            var dirString = args[0].Conv<string>();
            var speed = args[1].Conv<double>();

            /*
            * This function is weird.
            * dirString must be 9 characters long, and each character is a 1 or a 0.
            * Each character represents a direction. If multiple directions are set, a random one is picked.
            * Directions are as followed:
            * 0 - 225        Down-Left
            * 1 - 270        Down
            * 2 - 315        Down-Right
            * 3 - 180        Left
            * 4 - 0        Stop
            * 5 - 0        Right
            * 6 - 135        Up-Left
            * 7 - 90        Up
            * 8 - 45        Up-Right
            */

            if (dirString.Length != 9)
            {
                throw new InvalidOperationException("dirString must be 9 characters long");
            }

            if (Action_Relative)
            {
                speed = VMExecutor.Self.GMSelf.speed + speed;
            }

            VMExecutor.Self.GMSelf.speed = speed;

            int dir;
            do
            {
                dir = (int)GMRandom.YYRandom(9);
            } while (dirString[dir] != '1');

            switch (dir)
            {
                case 0:
                    VMExecutor.Self.GMSelf.direction = 255;
                    break;
                case 1:
                    VMExecutor.Self.GMSelf.direction = 270;
                    break;
                case 2:
                    VMExecutor.Self.GMSelf.direction = 315;
                    break;
                case 3:
                    VMExecutor.Self.GMSelf.direction = 180;
                    break;
                case 4:
                    VMExecutor.Self.GMSelf.direction = 0;
                    VMExecutor.Self.GMSelf.speed = 0;
                    break;
                case 5:
                    VMExecutor.Self.GMSelf.direction = 0;
                    break;
                case 6:
                    VMExecutor.Self.GMSelf.direction = 135;
                    break;
                case 7:
                    VMExecutor.Self.GMSelf.direction = 90;
                    break;
                case 8:
                    VMExecutor.Self.GMSelf.direction = 56;
                    break;
            }

            return null;
        }

        [GMLFunction("action_color")]
        public static object? action_color(object?[] args) => GraphicFunctions.draw_set_colour(args);

        [GMLFunction("action_inherited")]
        public static object? action_inherited(object?[] args) => MiscFunctions.event_inherited(args);

        [GMLFunction("action_set_sprite")]
        public static object? action_set_sprite(object?[] args)
        {
            var sprite = args[0].Conv<int>();
            var scale = args[1].Conv<double>();
            var self = VMExecutor.Self.GMSelf;

            self.sprite_index = sprite;
            if (scale > 0)
            {
                self.image_xscale = scale;
                self.image_yscale = scale;
            }

            return null;
        }

        [GMLFunction("action_sprite_color")]
        public static object? action_sprite_color(object?[] args)
        {
            var self = VMExecutor.Self.GMSelf;
            self.image_blend = args[0].Conv<int>();
            self.image_alpha = args[1].Conv<double>();
            return null;
        }

        // Real GM 8.0/8.1 don't implement this one either -- same "editor-only, never wired to the
        // compiled game" category as action_fill_color/action_draw_font above.
        [GMLFunction("action_path_old")]
        public static object? action_path_old(object?[] args) => null;

        [GMLFunction("action_path_end")]
        public static object? action_path_end(object?[] args) => GameFunctions.path_end(args);

        [GMLFunction("action_path_position")]
        public static object? action_path_position(object?[] args)
        {
            var position = args[0].Conv<double>();
            var self = VMExecutor.Self.GMSelf;
            self.path_position = Action_Relative ? self.path_position + position : position;
            return null;
        }

        [GMLFunction("action_path_speed")]
        public static object? action_path_speed(object?[] args)
        {
            var speed = args[0].Conv<double>();
            var self = VMExecutor.Self.GMSelf;
            self.path_speed = Action_Relative ? self.path_speed + speed : speed;
            return null;
        }

        [GMLFunction("action_move_contact")]
        public static object? action_move_contact(object?[] args)
        {
            var direction = args[0];
            var maxDistance = args[1];
            var kind = args[2].Conv<int>();

            return kind == 0
                ? GameFunctions.move_contact_solid(new[] { direction, maxDistance })
                : GameFunctions.move_contact_all(new[] { direction, maxDistance });
        }

        [GMLFunction("action_set_alarm", before: "2.0.0.0")]
        public static object? action_set_alarm(object?[] args)
        {
            var value = args[0].Conv<int>();
            var index = args[1].Conv<int>();

            if (Action_Relative)
            {
                var curValue = VMExecutor.Self.GMSelf.alarm[index];
                if (curValue > -1)
                {
                    VMExecutor.Self.GMSelf.alarm[index] = curValue + value;
                    return null;
                }
            }

            VMExecutor.Self.GMSelf.alarm[index] = value;
            return null;
        }

        [GMLFunction("action_set_friction", before: "2.0.0.0")]
        public static object? action_set_friction(object?[] args)
        {
            var friction = args[0].Conv<double>();

            if (Action_Relative)
            {
                friction += VMExecutor.Self.GMSelf.friction;
            }

            VMExecutor.Self.GMSelf.friction = friction;
            return null;
        }

        [GMLFunction("action_set_gravity", before: "2.0.0.0")]
        public static object? action_set_gravity(object?[] args)
        {
            var gravity = args[0].Conv<double>();

            if (Action_Relative)
            {
                gravity += VMExecutor.Self.GMSelf.gravity;
            }

            VMExecutor.Self.GMSelf.gravity = gravity;
            return null;
        }

        [GMLFunction("action_set_hspeed", before: "2.0.0.0")]
        public static object? action_set_hspeed(object?[] args)
        {
            var hspeed = args[0].Conv<double>();

            if (Action_Relative)
            {
                hspeed += VMExecutor.Self.GMSelf.hspeed;
            }

            VMExecutor.Self.GMSelf.hspeed = hspeed;
            return null;
        }

        [GMLFunction("action_set_vspeed", before: "2.0.0.0")]
        public static object? action_set_vspeed(object?[] args)
        {
            var vspeed = args[0].Conv<double>();

            if (Action_Relative)
            {
                vspeed += VMExecutor.Self.GMSelf.vspeed;
            }

            VMExecutor.Self.GMSelf.vspeed = vspeed;
            return null;
        }

        [GMLFunction("action_move_to", before: "2.0.0.0")]
        public static object? action_move_to(object?[] args)
        {
            var x = args[0].Conv<double>();
            var y = args[1].Conv<double>();

            if (Action_Relative)
            {
                x += VMExecutor.Self.GMSelf.x;
                y += VMExecutor.Self.GMSelf.y;
            }

            VMExecutor.Self.GMSelf.x = x;
            VMExecutor.Self.GMSelf.y = y;
            return null;
        }

        // ---- Room / game navigation ----

        [GMLFunction("action_another_room")]
        public static object? action_another_room(object?[] args) => GameFunctions.room_goto(args);

        [GMLFunction("action_current_room")]
        public static object? action_current_room(object?[] args) => GameFunctions.room_restart(args);

        [GMLFunction("action_previous_room")]
        public static object? action_previous_room(object?[] args) => GameFunctions.room_goto_previous(args);

        [GMLFunction("action_next_room")]
        public static object? action_next_room(object?[] args) => GameFunctions.room_goto_next(args);

        [GMLFunction("action_if_previous_room")]
        public static object? action_if_previous_room(object?[] args)
            => RoomManager.room_previous(RoomManager.CurrentRoom.AssetId) != -1;

        [GMLFunction("action_if_next_room")]
        public static object? action_if_next_room(object?[] args)
            => RoomManager.room_next(RoomManager.CurrentRoom.AssetId) != -1;

        [GMLFunction("action_restart_game")]
        public static object? action_restart_game(object?[] args) => GameFunctions.game_restart(args);

        [GMLFunction("action_end_game")]
        public static object? action_end_game(object?[] args) => GameFunctions.game_end(args);

        [GMLFunction("action_create_object_random")]
        public static object? action_create_object_random(object?[] args)
        {
            var candidates = new[] { args[0].Conv<int>(), args[1].Conv<int>(), args[2].Conv<int>(), args[3].Conv<int>() }
                .Where(InstanceManager.ObjectDefinitions.ContainsKey)
                .ToArray();

            if (candidates.Length == 0)
            {
                return null;
            }

            var obj = candidates[(int)GMRandom.YYRandom(candidates.Length)];
            var (x, y) = RelativeOffset(args[4].Conv<double>(), args[5].Conv<double>());
            return CreateObjectInstance(obj, x, y);
        }

        // ---- Conditionals ----

        [GMLFunction("action_if_collision")]
        public static object? action_if_collision(object?[] args)
        {
            var (x, y) = RelativeOffset(args[0].Conv<double>(), args[1].Conv<double>());
            var checkAll = args[2].Conv<bool>();
            return checkAll
                ? !GameFunctions.place_empty([x, y]).Conv<bool>()
                : !GameFunctions.place_free([x, y]).Conv<bool>();
        }

        [GMLFunction("action_if_empty")]
        public static object? action_if_empty(object?[] args) => !action_if_collision(args).Conv<bool>();

        [GMLFunction("action_if_number")]
        public static object? action_if_number(object?[] args)
        {
            var obj = args[0].Conv<int>();
            var number = args[1].Conv<int>();
            var comparator = args[2].Conv<int>();
            var count = InstanceManager.instance_number(obj);

            return comparator switch
            {
                1 => count < number,
                2 => count > number,
                _ => count == number,
            };
        }

        [GMLFunction("action_if_object")]
        public static object? action_if_object(object?[] args)
        {
            var obj = args[0];
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            return GameFunctions.place_meeting([x, y, obj]);
        }

        [GMLFunction("action_if_question")]
        public static object? action_if_question(object?[] args) => InteractionFunctions.show_question(args);

        [GMLFunction("action_if_dice")]
        public static object? action_if_dice(object?[] args)
        {
            var sides = args[0].Conv<double>();
            return MathFunctions.random([sides]).Conv<double>() < 1.0;
        }

        [GMLFunction("action_if_mouse")]
        public static object? action_if_mouse(object?[] args)
        {
            // 1/2/3 are left/right/middle (matching mouse_check_button's own numbering); anything
            // else is the D&D dropdown's "no button" choice.
            var button = args[0].Conv<int>();
            if (button is < 1 or > 3)
            {
                return InteractionFunctions.mouse_check_button([0]);
            }

            return InteractionFunctions.mouse_check_button([button]).Conv<bool>()
                || InteractionFunctions.mouse_check_button_released([button]).Conv<bool>();
        }

        [GMLFunction("action_if_aligned")]
        public static object? action_if_aligned(object?[] args)
        {
            var xsnap = args[0].Conv<double>();
            var ysnap = args[1].Conv<double>();
            var self = VMExecutor.Self.GMSelf;

            return (xsnap <= 0 || self.x % xsnap == 0) && (ysnap <= 0 || self.y % ysnap == 0);
        }

        [GMLFunction("action_if_variable")]
        public static object? action_if_variable(object?[] args)
        {
            var lhs = args[0];
            var rhs = args[1];
            var comparator = args[2].Conv<int>();

            if (lhs is string || rhs is string)
            {
                var cmp = string.CompareOrdinal(lhs.Conv<string>(), rhs.Conv<string>());
                return comparator switch
                {
                    1 => cmp < 0,
                    2 => cmp > 0,
                    _ => cmp == 0,
                };
            }

            var l = lhs.Conv<double>();
            var r = rhs.Conv<double>();
            return comparator switch
            {
                1 => l < r,
                2 => l > r,
                _ => CustomMath.ApproxEqual(l, r),
            };
        }

        [GMLFunction("action_if_sound")]
        public static object? action_if_sound(object?[] args) => SoundFunctionsLegacy.sound_isplaying(args);

        // ---- Score / lives / health ----

        [GMLFunction("action_set_score")]
        public static object? action_set_score(object?[] args)
        {
            var score = args[0].Conv<double>();
            VariableResolver.set_score(Action_Relative ? VariableResolver.get_score(0).Conv<double>() + score : score, 0);
            return null;
        }

        [GMLFunction("action_if_score")]
        public static object? action_if_score(object?[] args)
        {
            var value = args[0].Conv<double>();
            var method = args[1].Conv<int>();
            var score = VariableResolver.get_score(0).Conv<double>();

            return method switch
            {
                1 => score < value,
                2 => score > value,
                _ => CustomMath.ApproxEqual(score, value),
            };
        }

        [GMLFunction("action_draw_score")]
        public static object? action_draw_score(object?[] args)
        {
            var (x, y) = RelativeOffset(args[0].Conv<double>(), args[1].Conv<double>());
            var caption = args[2].Conv<string>();
            return GraphicFunctions.draw_text([x, y, caption + VariableResolver.get_score(0)]);
        }

        [GMLFunction("action_set_life")]
        public static object? action_set_life(object?[] args)
        {
            var lives = args[0].Conv<double>();
            VariableResolver.set_lives(Action_Relative ? VariableResolver.get_lives(0).Conv<double>() + lives : lives, 0);
            return null;
        }

        [GMLFunction("action_if_life")]
        public static object? action_if_life(object?[] args)
        {
            var value = args[0].Conv<double>();
            var method = args[1].Conv<int>();
            var lives = VariableResolver.get_lives(0).Conv<double>();

            return method switch
            {
                1 => lives < value,
                2 => lives > value,
                _ => CustomMath.ApproxEqual(lives, value),
            };
        }

        [GMLFunction("action_draw_life")]
        public static object? action_draw_life(object?[] args)
        {
            var (x, y) = RelativeOffset(args[0].Conv<double>(), args[1].Conv<double>());
            var caption = args[2].Conv<string>();
            return GraphicFunctions.draw_text([x, y, caption + VariableResolver.get_lives(0)]);
        }

        [GMLFunction("action_draw_life_images")]
        public static object? action_draw_life_images(object?[] args)
        {
            var (x, y) = RelativeOffset(args[0].Conv<double>(), args[1].Conv<double>());
            var sprite = args[2].Conv<int>();
            var lives = (int)VariableResolver.get_lives(0).Conv<double>();

            var spriteData = SpriteManager.GetSpriteAsset(sprite);
            if (spriteData == null)
            {
                return null;
            }

            for (var i = 0; i < lives; i++)
            {
                GraphicFunctions.draw_sprite([sprite, 0, x + (i * spriteData.Width), y]);
            }

            return null;
        }

        [GMLFunction("action_set_health")]
        public static object? action_set_health(object?[] args)
        {
            var health = args[0].Conv<double>();
            VariableResolver.set_health(Action_Relative ? VariableResolver.get_health(0).Conv<double>() + health : health, 0);
            return null;
        }

        [GMLFunction("action_if_health")]
        public static object? action_if_health(object?[] args)
        {
            var value = args[0].Conv<double>();
            var method = args[1].Conv<int>();
            var health = VariableResolver.get_health(0).Conv<double>();

            return method switch
            {
                1 => health < value,
                2 => health > value,
                _ => CustomMath.ApproxEqual(health, value),
            };
        }

        // BGR-encoded (0xBBGGRR, matching make_color_rgb's own r | g<<8 | b<<16) so these can be
        // compared/passed straight into draw_rectangle's existing colour handling without a lookup
        // table -- ported from GM8's own action_draw_health colour ramp (18 fixed choices plus a
        // green-to-red / white-to-black computed ramp), which OpenGM has no other reason to expose
        // as reusable constants.
        private static readonly int[] HealthBarPalette =
        {
            0x000000, // c_black
            0x808080, // c_gray
            0xC0C0C0, // c_silver
            0xFFFFFF, // c_white
            0x000080, // c_maroon
            0x008000, // c_green
            0x008080, // c_olive
            0x800000, // c_navy
            0x800080, // c_purple
            0x808000, // c_teal
            0x0000FF, // c_red
            0x00FF00, // c_lime
            0x00FFFF, // c_yellow
            0xFF0000, // c_blue
            0xFF00FF, // c_fuchsia
            0xFFFF00, // c_aqua
        };

        [GMLFunction("action_draw_health")]
        public static object? action_draw_health(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();
            var backCol = args[4].Conv<int>();
            var col = args[5].Conv<int>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            var healthRatio = Math.Clamp(VariableResolver.get_health(0).Conv<double>() / 100.0, 0.0, 1.0);

            int barColour;
            if (col == 0)
            {
                // green (full health) to red (empty), computed directly rather than via GM8's own
                // byte-twiddling -- same result, easier to read.
                var g = (byte)Math.Clamp(healthRatio > 0.5 ? 255.0 : healthRatio * 2 * 255, 0, 255);
                var r = (byte)Math.Clamp(healthRatio > 0.5 ? (1.0 - healthRatio) * 2 * 255 : 255.0, 0, 255);
                barColour = r | (g << 8);
            }
            else if (col == 1)
            {
                // white (full) to black (empty)
                var v = (byte)(healthRatio * 255);
                barColour = v | (v << 8) | (v << 16);
            }
            else
            {
                var index = col - 2;
                barColour = index >= 0 && index < HealthBarPalette.Length ? HealthBarPalette[index] : HealthBarPalette[0];
            }

            // draw_rectangle has no colour argument of its own -- it paints with whatever
            // draw_set_color last set -- so the bar/background colours have to go through that
            // global draw state, restored to whatever it was before this action ran.
            var previousColor = GraphicFunctions.draw_get_colour(Array.Empty<object?>()).Conv<int>();

            if (backCol != 0)
            {
                var index = backCol - 1;
                var backColour = index >= 0 && index < HealthBarPalette.Length ? HealthBarPalette[index] : HealthBarPalette[8];
                GraphicFunctions.draw_set_colour(new object?[] { backColour });
                GraphicFunctions.draw_rectangle(new object?[] { x1, y1, x2, y2, false });
                GraphicFunctions.draw_rectangle(new object?[] { x1, y1, x2, y2, true });
            }

            var barX2 = x1 + ((x2 - x1) * healthRatio);
            GraphicFunctions.draw_set_colour(new object?[] { barColour });
            GraphicFunctions.draw_rectangle(new object?[] { x1, y1, barX2, y2, false });
            GraphicFunctions.draw_rectangle(new object?[] { x1, y1, x2, y2, true });

            GraphicFunctions.draw_set_colour(new object?[] { previousColor });
            return null;
        }

        // ---- Sprite / cursor / misc instance ----

        [GMLFunction("action_kill_position")]
        public static object? action_kill_position(object?[] args)
        {
            var (x, y) = RelativeOffset(args[0].Conv<double>(), args[1].Conv<double>());

            // Real GM8's position_destroy kills every instance overlapping the point, not just one
            // -- this walks instances directly the same way
            // CollisionManager's own point-collision helpers do.
            foreach (var instance in InstanceManager.instances.Values.ToList())
            {
                if (!instance.Marked && instance.Active && CollisionManager.Collision_Point(instance, x, y, true))
                {
                    GameFunctions.instance_destroy(new object?[] { instance.instanceId, true });
                }
            }

            return null;
        }

        [GMLFunction("action_move_start")]
        public static object? action_move_start(object?[] args)
        {
            var self = VMExecutor.Self.GMSelf;
            self.x = self.xstart;
            self.y = self.ystart;
            return null;
        }

        [GMLFunction("action_wrap")]
        public static object? action_wrap(object?[] args)
        {
            // GM8's D&D dropdown: 0 = horizontal, 1 = vertical, else both -- move_wrap itself takes
            // two separate bools plus a margin (0 = wrap exactly at the room edge), not this one
            // selector.
            var selector = args[0].Conv<int>();
            var (horizontal, vertical) = selector switch
            {
                0 => (true, false),
                1 => (false, true),
                _ => (true, true),
            };

            return GameFunctions.move_wrap(new object?[] { horizontal, vertical, 0.0 });
        }

        [GMLFunction("action_snap")]
        public static object? action_snap(object?[] args) => GameFunctions.move_snap(args);

        [GMLFunction("action_execute_script")]
        public static object? action_execute_script(object?[] args) => ResourceFunctions.script_execute(args);

        [GMLFunction("action_message")]
        public static object? action_message(object?[] args) => InteractionFunctions.show_message(args);

        [GMLFunction("action_fullscreen")]
        public static object? action_fullscreen(object?[] args)
        {
            // 0 = toggle, 1 = windowed, 2 = fullscreen (GM8's own D&D dropdown order).
            var mode = args[0].Conv<int>();
            var fullscreen = mode switch
            {
                1 => false,
                2 => true,
                _ => !GraphicFunctions.window_get_fullscreen(Array.Empty<object?>()).Conv<bool>(),
            };

            return GraphicFunctions.window_set_fullscreen([fullscreen]);
        }

        [GMLFunction("action_font")]
        public static object? action_font(object?[] args)
        {
            GraphicFunctions.draw_set_font(args[0..1]);
            GraphicFunctions.draw_set_halign(args[1..2]);
            return null;
        }

        // GM 8.0/8.1's own action_fill_color, action_line_color, action_draw_font and
        // action_draw_font_old don't do anything either -- these D&D actions were only ever used to
        // set state consumed by an in-editor preview, not by the compiled game (confirmed against
        // OpenGMK's kernel.rs, which stubs all four the same way with the same note). Registering
        // them (instead of leaving them unresolved) still matters: an unresolved call aborts the
        // whole calling event, and these were commonly placed ahead of other drawing actions in the
        // same event.
        [GMLFunction("action_fill_color")]
        public static object? action_fill_color(object?[] args) => null;

        [GMLFunction("action_line_color")]
        public static object? action_line_color(object?[] args) => null;

        [GMLFunction("action_draw_font")]
        public static object? action_draw_font(object?[] args) => null;

        [GMLFunction("action_draw_font_old")]
        public static object? action_draw_font_old(object?[] args) => null;

        [GMLFunction("action_set_cursor", GMLFunctionFlags.Stub)]
        public static object? action_set_cursor(object?[] args) => null;

        [GMLFunction("action_set_caption", GMLFunctionFlags.Stub)]
        public static object? action_set_caption(object?[] args) => null;

        [GMLFunction("action_replace_sprite", GMLFunctionFlags.Stub)]
        public static object? action_replace_sprite(object?[] args) => null;

        [GMLFunction("action_replace_sound", GMLFunctionFlags.Stub)]
        public static object? action_replace_sound(object?[] args) => null;

        [GMLFunction("action_replace_background", GMLFunctionFlags.Stub)]
        public static object? action_replace_background(object?[] args) => null;

        [GMLFunction("action_sleep", GMLFunctionFlags.Stub)]
        public static object? action_sleep(object?[] args) => null;

        [GMLFunction("action_webpage", GMLFunctionFlags.Stub)]
        public static object? action_webpage(object?[] args) => null;

        // ---- Drawing ----

        [GMLFunction("action_draw_sprite")]
        public static object? action_draw_sprite(object?[] args)
        {
            var sprite = args[0];
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            var imageIndex = args[3];
            return GraphicFunctions.draw_sprite([sprite, imageIndex, x, y]);
        }

        [GMLFunction("action_draw_background")]
        public static object? action_draw_background(object?[] args)
        {
            var bg = args[0];
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            // The 4th arg (tiled) has no effect: draw_background itself doesn't tile yet either
            // (see its own TODO in ScriptResolver.cs) -- not a new limitation introduced here.
            return ScriptResolver.draw_background([bg, x, y]);
        }

        [GMLFunction("action_draw_text")]
        public static object? action_draw_text(object?[] args)
        {
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            return GraphicFunctions.draw_text([x, y, args[0]]);
        }

        [GMLFunction("action_draw_text_transformed")]
        public static object? action_draw_text_transformed(object?[] args)
        {
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            return GraphicFunctions.draw_text_transformed([x, y, args[0], args[3], args[4], args[5]]);
        }

        [GMLFunction("action_draw_variable")]
        public static object? action_draw_variable(object?[] args)
        {
            var (x, y) = RelativeOffset(args[1].Conv<double>(), args[2].Conv<double>());
            return GraphicFunctions.draw_text([x, y, args[0].Conv<string>()]);
        }

        [GMLFunction("action_draw_line")]
        public static object? action_draw_line(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            return GraphicFunctions.draw_line([x1, y1, x2, y2]);
        }

        [GMLFunction("action_draw_arrow")]
        public static object? action_draw_arrow(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            return GraphicFunctions.draw_arrow([x1, y1, x2, y2, args[4]]);
        }

        [GMLFunction("action_draw_rectangle")]
        public static object? action_draw_rectangle(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            return GraphicFunctions.draw_rectangle(new object?[] { x1, y1, x2, y2, args[4] });
        }

        [GMLFunction("action_draw_ellipse")]
        public static object? action_draw_ellipse(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            return GraphicFunctions.draw_ellipse(new object?[] { x1, y1, x2, y2, args[4] });
        }

        [GMLFunction("action_draw_gradient_hor")]
        public static object? action_draw_gradient_hor(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            var col1 = args[4];
            var col2 = args[5];
            return GraphicFunctions.draw_rectangle_colour(new[] { x1 as object, y1, x2, y2, col1, col2, col2, col1, false });
        }

        [GMLFunction("action_draw_gradient_vert")]
        public static object? action_draw_gradient_vert(object?[] args)
        {
            var x1 = args[0].Conv<double>();
            var y1 = args[1].Conv<double>();
            var x2 = args[2].Conv<double>();
            var y2 = args[3].Conv<double>();

            if (Action_Relative)
            {
                var self = VMExecutor.Self.GMSelf;
                x1 += self.x;
                x2 += self.x;
                y1 += self.y;
                y2 += self.y;
            }

            var col1 = args[4];
            var col2 = args[5];
            return GraphicFunctions.draw_rectangle_colour(new[] { x1 as object, y1, x2, y2, col1, col1, col2, col2, false });
        }
    }
}
