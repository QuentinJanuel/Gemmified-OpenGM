using OpenGM.IO;
using OpenGM.Loading;
using OpenGM.Rendering;
using OpenGM.SerializedFiles;
using OpenGM.VirtualMachine;
using OpenTK.Mathematics;
using UndertaleModLib.Models;
using EventType = OpenGM.VirtualMachine.EventType;

namespace OpenGM;

/// <summary>
/// instance of an ObjectDefinition
///
/// CInstance in cpp
///
/// TODO: make this derive from YYObjectBase, remove interface
/// </summary>
public class GamemakerObject : DrawWithDepth, IStackContextSelf
{
    public override void Draw()
    {

    }

    /// <summary>
    /// CHECK BUILTIN SELF VARS BEFORE THIS!!
    /// </summary>
    public Dictionary<string, object?> SelfVariables { get; } = new();

    public ObjectDefinition Definition;

    public int object_index => Definition.AssetId;
    public int[] alarm = Enumerable.Repeat(-1, 12).ToArray();

    public bool persistent = false;

    private bool _solid = false;

    /// <summary>
    /// Kept in sync with <see cref="InstanceManager.SolidInstances"/> on every assignment (from
    /// construction, GML's <c>solid</c> variable, or object_set_solid) so
    /// CollisionManager.IsCollidingAtCurrentPosition's solid-only queries -- the hot path behind
    /// move_contact_solid/move_bounce_solid/move_outside_solid and place_free -- can iterate just
    /// the actually-solid instances instead of every instance in the room (including however many
    /// hundred non-solid particles a game's death effect happens to have left lying around).
    /// </summary>
    public bool solid
    {
        get => _solid;
        set
        {
            if (_solid == value)
            {
                return;
            }

            _solid = value;

            if (value)
            {
                InstanceManager.SolidInstances.Add(this);
            }
            else
            {
                InstanceManager.SolidInstances.Remove(this);
            }
        }
    }

    private double _x;
    public double x
    {
        get => _x;

        set
        {
            _x = value;
            bbox_dirty = true;
        }
    }
    private double _y;
    public double y
    {
        get => _y;

        set
        {
            _y = value;
            bbox_dirty = true;
        }
    }
    public double xprevious;
    public double yprevious;
    public double xstart;
    public double ystart;
    public bool visible = true;
    public Vector4i margins = Vector4i.Zero;
    public double image_speed = 1;

    private double _image_xscale = 1;
    public double image_xscale
    {
        get => _image_xscale;
        set
        {
            _image_xscale = value;
            bbox_dirty = true;
        }
    }

    private double _image_yscale = 1;
    public double image_yscale
    {
        get => _image_yscale;
        set
        {
            _image_yscale = value;
            bbox_dirty = true;
        }
    }

    private double _image_angle;
    public double image_angle
    {
        get => _image_angle;
        set
        {
            _image_angle = value;
            bbox_dirty = true;
        }
    }

    private double _image_index;
    public double image_index
    {
        get => _image_index;
        set
        {
            var prevValue = _image_index;
            _image_index = value;

            if ((int)_image_index == (int)prevValue)
            {
                return;
            }

            if (mask_index != -1)
            {
                return;
            }

            bbox_dirty = true;
            /*
                        if (margins != Vector4.Zero)
                        {
                            var spriteAsset = mask_index == -1
                                ? SpriteManager.GetSpriteAsset(sprite_index)
                                : SpriteManager.GetSpriteAsset(mask_index);

                            if (spriteAsset != null && spriteAsset.CollisionMasks.Count == 1 && CollisionManager. colliders.Any(x => x.GMObject == this))
                            {
                                // Don't regenerate collider when theres only one mask, dummy
                                return;
                            }

                            CollisionManager.RegisterCollider(this, margins);
                        }*/
        }
    }

    private int _cachedSpriteWidth;
    private int _cachedSpriteHeight;
    public double sprite_width => _cachedSpriteWidth * image_xscale;
    public double sprite_height => _cachedSpriteHeight * image_yscale;

    // todo : optimize!!!

    public bool bbox_dirty;

    //public BBox bbox => CollisionManager.colliders.First(x => x.GMObject == this).BBox;

    private BBox? _bbox = null;
    public BBox bbox
    {
        get
        {
            if (_bbox == null || bbox_dirty)
            {
                _bbox = CollisionManager.CalculateBoundingBox(this);
            }

            return _bbox;
        }

        set => _bbox = value;
    }
    public double bbox_left => bbox.left;
    public double bbox_right => bbox.right;
    public double bbox_top => bbox.top;
    public double bbox_bottom => bbox.bottom;

    private double _cached_sprite_xoffset;
    public double sprite_xoffset
        => sprite_index == -1
            ? x
            : _cached_sprite_xoffset;

    private double _cached_sprite_yoffset;
    public double sprite_yoffset
        => sprite_index == -1
            ? y
            : _cached_sprite_yoffset;

    private int _sprite_index = -1;
    public int sprite_index
    {
        get => _sprite_index;
        set
        {
            if (_sprite_index == value)
            {
                return;
            }

            if (value < 0)
            {
                // -1 (noone) is ordinary GML for "no sprite", not a mistake: an object whose sprite
                // is only an editor marker clears it in its Create event, and plenty of objects
                // toggle their sprite off this way. Refusing the write left the previous sprite
                // drawing forever -- tijit engine 2's camera object kept its eye marker sitting in
                // the corner of the room.
                _sprite_index = -1;
                _cachedSpriteWidth = 0;
                _cachedSpriteHeight = 0;
                _cached_sprite_xoffset = 0;
                _cached_sprite_yoffset = 0;

                // A sprite-less instance has no mask of its own to collide with either, unless
                // mask_index names one explicitly.
                if (mask_index == -1)
                {
                    margins = Vector4i.Zero;
                    bbox_dirty = true;
                }

                return;
            }

            _sprite_index = value;

            var sprite = SpriteManager.GetSpriteAsset(_sprite_index);

            if (sprite == null)
            {
                DebugLog.LogWarning($"Couldn't find sprite for index {_sprite_index}!");
                return;
            }

            // The sprite's DECLARED size, not the trimmed rect the texture page happens to hold.
            // GML's sprite_width/sprite_height are the same number sprite_get_width/_height report
            // (times the instance's scale), and those already read Width/Height -- so a trimmed
            // sprite made the two disagree. sprSpikeCeleste in tijit engine 2 is declared 32x32 but
            // packs as 30x10, and any geometry a game builds from sprite_width lands wrong by that
            // difference.
            _cachedSpriteWidth = sprite!.Width;
            _cachedSpriteHeight = sprite.Height;
            _cached_sprite_xoffset = sprite.Origin.X;
            _cached_sprite_yoffset = sprite.Origin.Y;

            if (mask_index != -1)
            {
                return;
            }

            if (sprite == null)
            {
                return;
            }

            margins = sprite.Margins;

            /*if (margins != Vector4.Zero)
            {
                CollisionManager.RegisterCollider(this, margins);
            }*/

            bbox_dirty = true;
        }
    }

    private int _mask_id = -1;
    public int mask_index
    {
        get => _mask_id;
        set
        {
            _mask_id = value;

            if (value == -1)
            {
                sprite_index = sprite_index; // force the setter to run again
                return;
            }

            var maskSprite = SpriteManager.GetSpriteAsset(_mask_id);

            if (maskSprite == null)
            {
                return;
            }

            margins = maskSprite.Margins;
            _cached_sprite_xoffset = maskSprite.Origin.X;
            _cached_sprite_yoffset = maskSprite.Origin.Y;

            /*if (margins != Vector4.Zero)
            {
                CollisionManager.RegisterCollider(this, margins);
            }*/

            bbox_dirty = true;
        }
    }

    public double gravity = 0;
    public double gravity_direction = 270;
    public double friction = 0;

    private double _speed;
    public double speed
    {
        get => _speed;
        set
        {
            if (_speed == value)
            {
                return;
            }

            _speed = value;
            ComputeHVSpeed();
        }
    }

    private double _hspeed;
    public double hspeed
    {
        get => _hspeed;
        set
        {
            if (_hspeed == value)
            {
                return;
            }

            _hspeed = value;
            ComputeSpeed();
        }
    }

    private double _vspeed;
    public double vspeed
    {
        get => _vspeed;
        set
        {
            if (_vspeed == value)
            {
                return;
            }

            _vspeed = value;
            ComputeSpeed();
        }
    }

    private double _direction;
    public double direction
    {
        get
        {
            return _direction;
        }

        set
        {
            var val = value;
            while (val < 0)
            {
                val += 360;
            }

            while (val > 360)
            {
                val -= 360;
            }

            _direction = CustomMath.FMod((float)val, 360);
            ComputeHVSpeed();
        }
    }

    // https://github.com/YoYoGames/GameMaker-HTML5/blob/9122bcd3a811bd4878a1f0bbce9e6b04b31bee31/scripts/yyInstance.js#L1131
    public void ComputeSpeed()
    {
        if (hspeed == 0)
        {
            if (vspeed > 0)
            {
                _direction = 270;
            }
            else if (vspeed < 0)
            {
                _direction = 90;
            }
        }
        else
        {
            var dd = CustomMath.ClampFloat((float)(180 * (Math.Atan2(vspeed, hspeed)) / Math.PI));
            _direction = dd <= 0
                ? (double)-dd
                : 360.0 - dd;
        }

        if (Math.Abs(_direction - CustomMath.Round(_direction)) < 0.0001)
        {
            _direction = CustomMath.Round(_direction);
        }
        _direction = CustomMath.FMod((float)_direction, 360f);

        _speed = Math.Sqrt((hspeed * hspeed) + (vspeed * vspeed));
        if (Math.Abs(_speed - CustomMath.Round(_speed)) < 0.0001)
        {
            _speed = CustomMath.Round(_speed);
        }
    }

    // https://github.com/YoYoGames/GameMaker-HTML5/blob/9122bcd3a811bd4878a1f0bbce9e6b04b31bee31/scripts/yyInstance.js#L1175
    public void ComputeHVSpeed()
    {
        _hspeed = speed * CustomMath.ClampFloat((float)Math.Cos(direction * CustomMath.Deg2Rad));
        _vspeed = -speed * CustomMath.ClampFloat((float)Math.Sin(direction * CustomMath.Deg2Rad));

        if (Math.Abs(_hspeed - CustomMath.Round(_hspeed)) < 0.0001)
        {
            _hspeed = CustomMath.Round(_hspeed);
        }

        if (Math.Abs(_vspeed - CustomMath.Round(_vspeed)) < 0.0001)
        {
            _vspeed = CustomMath.Round(_vspeed);
        }
    }

    public int image_blend = 16777215;
    public double image_alpha = 1;

    public bool _createRan;

    public bool Destroyed;

    public int path_index = -1;
    public double path_position = 0;
    public double path_previousposition;
    public PathEndAction path_endaction;
    public double path_speed;
    public double path_scale;
    public double path_xstart;
    public double path_ystart;
    public double path_orientation;

    public int timeline_index = -1;
    public double timeline_position;
    public double timeline_speed = 1;
    public bool timeline_running;
    public bool timeline_loop;

    public bool mouse_over;

    public bool Active = true;
    public bool NextActive = true; // Whether the object should be active or not next frame
    public bool Marked = false; // Marked for deletion at the end of the frame

    /// <summary>
    /// Destruction has begun but hasn't finished: the Destroy/CleanUp events are running right now,
    /// during which the instance is deliberately still visible to every lookup (see
    /// <see cref="InstanceManager.MarkForDestruction"/>). Purely a re-entrancy guard, so an
    /// instance that destroys itself again from inside its own Destroy event doesn't recurse.
    /// </summary>
    public bool Destroying = false;
    public bool IsOutsideRoom = false; // Store state so event isn't called multiple times

    public int Layer = -1;

    public GamemakerObject(ObjectDefinition obj, double x, double y, int depth, int instanceId, int spriteIndex, bool visible, bool persistent, int maskIndex)
    {
        Definition = obj;
        this.x = x;
        this.y = y;
        this.depth = depth;
        this.instanceId = instanceId;
        this.sprite_index = spriteIndex;
        this.visible = visible;
        this.persistent = persistent;
        this.mask_index = maskIndex;
        this.solid = obj.solid;

        xstart = x;
        ystart = y;

        Register();
    }

    public override void Destroy()
    {
        Destroyed = true;
        Unregister();
    }

    /// <summary>
    /// Adds this instance to the instance pool and registers it with DrawManager.
    /// </summary>
    public void Register()
    {
        InstanceManager.AddInstance(this);
        DrawManager.Register(this);

        /*if (margins != Vector4.Zero)
        {
            CollisionManager.RegisterCollider(this, margins);
        }*/
        bbox_dirty = true;
    }

    /// <summary>
    /// Removes this instance from the instance pool and unregisters it from DrawManager.
    /// </summary>
    public void Unregister()
    {
        InstanceManager.RemoveInstance(this);
        DrawManager.Unregister(this);
        //CollisionManager.UnregisterCollider(this);
    }

    // frame_overflow used to carry the amount a wrap had just subtracted through to DrawSelf, so the
    // draw could still show the frame the animation had been on when advance and wrap happened in
    // the same tick. Animate defers the wrap by a frame now, so image_index itself holds that value
    // for a whole step and the compensation would double-count it.

    /// <summary>
    /// Advances the sprite animation one step and wraps it, in that order and in that one operation.
    ///
    /// Verified against GameMaker Studio 1.4.9999 on Windows (a probe project). For a 4-frame sprite
    /// at image_speed 1, the Step event sees 1, 2, 3, 0, 1, 2, 3, 0 -- image_index is NEVER readable
    /// at image_number, and the animation-end event fires once per lap with image_index *already*
    /// wrapped to 0:
    ///
    ///     A step=3 image_index=3.00000000
    ///     A ANIM_END t=3 image_index=0.00000000
    ///     A step=4 image_index=0.00000000
    ///
    /// The accumulation is SINGLE precision, which is the whole ballgame. At image_speed 0.1 real GM
    /// reaches 3.99999833 after 40 steps and stops there -- that is float32's answer for 0.1 added 40
    /// times, to every one of the 8 digits it printed, where double gives exactly 4.0. Everything
    /// awkward about this follows from that one bit:
    ///
    ///   - 3.99999833 is BELOW the frame count, so nothing wraps and the sprite simply stays on
    ///     frame 3 (floor). Accumulated in double it lands on 4.000000000000002, which is >= 4, so it
    ///     wrapped and the sprite snapped back to frame 0 -- Spook Jam's Polar book slamming shut for
    ///     the 50 frames it is supposed to sit open.
    ///   - GML compares reals with a tolerance, so a game testing `image_index == 4` still matches at
    ///     3.99999833 (CustomMath.ApproxEqual, and GM's own epsilon must likewise exceed 1.7e-6).
    ///     That is how the "wait for the frame past the last one" idiom works at all -- 78 uses of
    ///     `image_index == n` in Spook Jam alone -- and it needs no special handling here.
    ///
    /// Earlier attempts deferred the wrap by a step, held the last frame in the draw path, and
    /// skipped the wrap for a stopped animation. All three were compensating for double-precision
    /// accumulation and are gone.
    /// </summary>
    public void Animate()
    {
        // Deliberately float, not double -- see above. `(float)a + (float)b` is float arithmetic;
        // widening the result back into the double field afterwards keeps the value GM would hold.
        image_index = (float)image_index + (float)GetIndexIncrement();

        var sprite = SpriteManager.GetSpriteAsset(sprite_index);

        if (sprite == null)
        {
            return;
        }

        var frameCount = sprite.Textures.Count;

        if (frameCount <= 0)
        {
            return;
        }

        if (image_index >= frameCount)
        {
            image_index = (float)(image_index - frameCount);
            ExecuteEvent(this, Definition, EventType.Other, (int)EventSubtypeOther.AnimationEnd);
        }
        else if (image_index < 0)
        {
            // Running backwards wraps the same way, and fires the same event. OpenGMK models only
            // the forward branch, so this used to wrap silently -- but a shipped game settles it:
            // CoinStack 1000's hub doors play their sprite forward to open (image_speed = 0.2),
            // flip to -0.2 in the animation-end event, and then rely on animation end firing AGAIN
            // on the reverse pass to set the alarm that calls room_goto. That reverse branch is the
            // only place the door's room_goto is ever reached, so without this the door opens, the
            // transition plays, and the game sits in the hub forever with the sprite cycling
            // backwards -- and the branch would have to be dead code in a game that demonstrably
            // works on the real runner.
            image_index = (float)(image_index + frameCount);
            ExecuteEvent(this, Definition, EventType.Other, (int)EventSubtypeOther.AnimationEnd);
        }
    }

    public double GetIndexIncrement()
    {
        var sprite = SpriteManager.GetSpriteAsset(sprite_index);
        var increment = 0.0d;

        if (sprite == null)
        {
            increment += image_speed;
        }
        else
        {
            if (sprite.PlaybackSpeedType == AnimSpeedType.FramesPerGameFrame)
            {
                increment += image_speed * sprite.PlaybackSpeed;
            }
            else
            {
                increment += image_speed * sprite.PlaybackSpeed / TimingManager.FPS;
            }
        }

        return increment;
    }

    public static bool ExecuteEvent(GamemakerObject obj, ObjectDefinition? definition, EventType eventType, int eventNumber = 0)
    {
        if (definition == null)
        {
            DebugLog.LogError($"Tried to execute event {eventType} {eventNumber} on null definition! obj:{obj}");
            //Debug.Break();
            return false;
        }

        // A marked instance is gone as far as the rest of the frame is concerned, so it runs no
        // further events. Destroy and CleanUp are exempt because they're the events that do the
        // destroying: MarkForDestruction runs them and only then sets Marked, and CleanUp in
        // particular is dispatched at room/game end for instances already marked.
        if (obj.Marked && eventType != EventType.Destroy && eventType != EventType.CleanUp)
        {
            return false;
        }

        // TODO:
        //   objects that change active status shouldn't acknowledge that change
        //   until next frame
        if (!obj.Active)
        {
            return false;
        }

        //DebugLog.LogInfo($"Trying to execute {eventType} {eventNumber} on {obj.object_index} with definition {definition.Name}");

        bool TryExecute(VMCode? code)
        {
            if (code != null)
            {
                VMExecutor.ExecuteCode(code, obj, definition, eventType, eventNumber);
                return true;
            }
            else if (definition.parent != null)
            {
                return ExecuteEvent(obj, definition.parent, eventType, eventNumber);
            }

            // event not found, and no parent to check
            return false;
        }

        bool TryExecuteDict<T>(Dictionary<T, VMCode> dict, T index) where T : notnull
        {
            if (dict.TryGetValue(index, out var script))
            {
                VMExecutor.ExecuteCode(script, obj, definition, eventType, eventNumber);
                return true;
            }
            else if (definition.parent != null)
            {
                return ExecuteEvent(obj, definition.parent, eventType, eventNumber);
            }

            // event not found, and no parent to check
            return false;
        }

        DrawManager.EventType = eventType;
        DrawManager.EventNumber = eventNumber;

        switch (eventType)
        {
            case EventType.Create:
                return TryExecute(definition.CreateCode);
            case EventType.Destroy:
                return TryExecute(definition.DestroyScript);
            case EventType.Alarm:
                return TryExecuteDict(definition.AlarmScript, eventNumber);
            case EventType.Step:
                return TryExecuteDict(definition.StepScript, (EventSubtypeStep)eventNumber);
            case EventType.Collision:
                return TryExecuteDict(definition.CollisionScript, eventNumber);
            case EventType.Keyboard:
                return TryExecuteDict(definition.KeyboardScripts, (EventSubtypeKey)eventNumber);
            case EventType.Mouse:
                return TryExecuteDict(definition.MouseScripts, (EventSubtypeMouse)eventNumber);
            case EventType.Other:
                return TryExecuteDict(definition.OtherScript, (EventSubtypeOther)eventNumber);
            case EventType.Draw:
                return TryExecuteDict(definition.DrawScript, (EventSubtypeDraw)eventNumber);
            case EventType.KeyPress:
                return TryExecuteDict(definition.KeyPressScripts, (EventSubtypeKey)eventNumber);
            case EventType.KeyRelease:
                return TryExecuteDict(definition.KeyReleaseScripts, (EventSubtypeKey)eventNumber);
            // trigger
            case EventType.CleanUp:
                return TryExecute(definition.CleanUpScript);
            // gesture
            case EventType.PreCreate:
                return TryExecute(definition.PreCreateScript);
            case EventType.Trigger:
            case EventType.Gesture:
            default:
                DebugLog.LogError($"Event type {eventType} not implemented.");
                return false;
        }
    }

    public void UpdateAlarms()
    {
        for (var i = 0; i < alarm.Length; i++)
        {
            if (alarm[i] != -1)
            {
                alarm[i]--;

                if (alarm[i] == 0)
                {
                    ExecuteEvent(this, Definition, EventType.Alarm, i);

                    if (alarm[i] == 0)
                    {
                        alarm[i] = -1;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Advances this instance's timeline by timeline_speed and runs every moment the step crossed.
    /// Mirrors OpenGMK's Game::frame (gm8emulator/src/game.rs), which is the reference for the
    /// ordering quirks below.
    /// </summary>
    public void UpdateTimeline()
    {
        if (!timeline_running)
        {
            return;
        }

        if (!GameLoader.Timelines.TryGetValue(timeline_index, out var timeline) || timeline.Moments.Count == 0)
        {
            return;
        }

        var length = timeline.Moments.Keys.Max();
        var oldPosition = timeline_position;
        var speed = timeline_speed;
        var newPosition = oldPosition + speed;

        if (newPosition > length && timeline_loop)
        {
            timeline_position = speed >= 0 ? 0 : length;
        }
        else
        {
            timeline_position = newPosition;
        }

        // Which moments are due is decided from the *un-clamped* newPosition, not from the wrapped
        // timeline_position written above -- so the frame a loop wraps on still runs the moments out
        // to the end of the timeline, and only the next frame starts the new lap from 0.
        List<KeyValuePair<int, List<VMCode>>> due;
        if (speed > 0)
        {
            due = timeline.Moments.Where(x => x.Key >= oldPosition && x.Key < newPosition).OrderBy(x => x.Key).ToList();
        }
        else if (speed < 0)
        {
            due = timeline.Moments.Where(x => x.Key <= oldPosition && x.Key > newPosition).OrderByDescending(x => x.Key).ToList();
        }
        else
        {
            // A stopped-but-running timeline holds position and fires nothing, rather than
            // re-running whatever moment it's parked on every frame.
            return;
        }

        foreach (var (_, actions) in due)
        {
            // ToList so a moment that edits this very timeline (timeline_moment_add_script and
            // friends are callable from inside one) doesn't invalidate the iteration.
            foreach (var code in actions.ToList())
            {
                // A moment can destroy this instance (or change room) out from under the rest of
                // the list -- ExecuteEvent makes the same check for ordinary events, but moments
                // don't go through it, so it has to happen here.
                if (Marked || Destroyed || !RoomManager.RoomLoaded)
                {
                    return;
                }

                VMExecutor.ExecuteCode(code, this, Definition);
            }
        }
    }

    private double VectorToDir(double gmHoriz, double gmVert)
    {
        if (gmHoriz >= 0 && gmVert == 0)
        {
            return 0;
        }

        if (gmHoriz > 0 && gmVert == 0)
        {
            return 0;
        }

        if (gmHoriz == 0 && gmVert < 0)
        {
            return 90;
        }

        // +gmVert means down, -gmVert means up
        gmVert = -gmVert;

        var angle = Math.Atan(gmVert / gmHoriz) * CustomMath.Rad2Deg;

        if (gmVert > 0)
        {
            if (gmHoriz > 0)
            {
                return angle;
            }

            return angle + 180;
        }

        if (gmHoriz > 0)
        {
            return 360 + angle;
        }

        return 180 + angle;
    }

    public void AdaptSpeed()
    {
        if (friction != 0)
        {
            var newSpeed = speed > 0
                ? speed - friction
                : speed + friction;

            if (speed > 0 && newSpeed < 0)
            {
                speed = 0;
            }
            else if (speed < 0 && newSpeed > 0)
            {
                speed = 0;
            }
            else if (speed != 0)
            {
                speed = newSpeed;
            }
        }

        if (gravity != 0)
        {
            vspeed -= Math.Sin(CustomMath.Deg2Rad * gravity_direction) * gravity;
            hspeed += Math.Cos(CustomMath.Deg2Rad * gravity_direction) * gravity;
        }
    }

    public void AssignPath(int pathIndex, double pathSpeed, double pathScale, double pathOrientation, bool absolute, PathEndAction endAction)
    {
        path_index = -1;

        if (pathIndex < 0)
        {
            return;
        }

        var path = PathManager.Paths[pathIndex];

        if (path == null)
        {
            return;
        }

        if (path.length <= 0)
        {
            return;
        }

        if (pathScale < 0)
        {
            return;
        }

        path_index = pathIndex;

        path_speed = pathSpeed;
        if (path_speed >= 0)
        {
            path_position = 0;
        }
        else
        {
            path_position = 1;
        }

        path_previousposition = path_position;
        path_scale = pathScale;

        path_orientation = pathOrientation;
        path_endaction = endAction;

        if (absolute)
        {
            x = path.XPosition(path_speed);
            y = path.YPosition(path_speed);

            path_xstart = path.XPosition(0);
            path_ystart = path.YPosition(0);
        }
        else
        {
            path_xstart = x;
            path_ystart = y;
        }
    }

    public bool AdaptPath()
    {
        if (path_index < 0)
        {
            return false;
        }

        if (!PathManager.Paths.ContainsKey(path_index))
        {
            return false;
        }

        var path = PathManager.Paths[path_index];

        if (path.length <= 0)
        {
            return false;
        }

        var atPathEnd = false;
        var orient = path_orientation * Math.PI / 180.0;

        var point = PathManager.GetPosition(path, path_position);
        var sp = point.speed;

        sp /= (100 * path_scale); // scale speed
        path_position += path_speed * sp / path.length; // increase position

        var point0 = PathManager.GetPosition(path, 0);

        if (path_position >= 1 || path_position <= 0)
        {
            atPathEnd = path_speed != 0;
            switch (path_endaction)
            {
                case PathEndAction.path_action_stop:
                    // TODO : why is this check not in the default case?
                    if (path_speed != 0)
                    {
                        path_position = 1;
                        path_index = -1;
                    }

                    break;
                case PathEndAction.path_action_restart:
                    if (path_position < 0)
                    {
                        path_position++;
                    }
                    else
                    {
                        path_position--;
                    }

                    break;
                case PathEndAction.path_action_continue:
                    var point1 = PathManager.GetPosition(path, 1);
                    var dx = point1.x - point0.x;
                    var dy = point1.y - point0.y;

                    var xdif = path_scale * ((dx * Math.Cos(orient)) + (dy * Math.Sin(orient)));
                    var ydif = path_scale * ((dy * Math.Cos(orient)) - (dx * Math.Sin(orient)));

                    if (path_position < 0)
                    {
                        path_xstart -= xdif;
                        path_ystart -= ydif;
                        path_position++;
                    }
                    else
                    {
                        path_xstart += xdif;
                        path_ystart += ydif;
                        path_position--;
                    }

                    break;
                case PathEndAction.path_action_reverse:
                    if (path_position < 0)
                    {
                        path_position = -path_position;
                        path_speed = Math.Abs(path_speed);
                    }
                    else
                    {
                        path_position = 2 - path_position;
                        path_speed = -Math.Abs(path_speed);
                    }

                    break;
                default:
                    path_position = 1;
                    path_index = -1;
                    break;
            }
        }

        point = PathManager.GetPosition(path, path_position);
        var xx = point.x - point0.x;
        var yy = point.y - point0.y;

        var newx = path_xstart + path_scale * ((xx * Math.Cos(orient)) + (yy * Math.Sin(orient)));
        var newy = path_ystart + path_scale * ((yy * Math.Cos(orient)) - (xx * Math.Sin(orient)));

        // trick to set the direction
        hspeed = newx - x;
        vspeed = newy - y;
        speed = 0;

        x = newx;
        y = newy;

        return atPathEnd;
    }

    public bool HasEvent(EventType eventType, int eventSubtype)
    {
        // Walks the parent chain -- a child object with no override of its own (e.g.
        // objCherryRandomCol, which only defines Create/Step and relies on parent objCherry's
        // Outside Room event to destroy itself once it leaves the room) inherits the event from
        // whichever ancestor actually defines it, same as ExecuteEvent already does when the leaf
        // definition doesn't have the event itself. Checking only the leaf Definition here (as
        // this used to) meant HasEvent-gated call sites (HandleOther's Outside Room check, etc.)
        // silently skipped every inherited-only event.
        for (var definition = Definition; definition != null; definition = definition.parent)
        {
            var has = eventType switch
            {
                EventType.Create => definition.CreateCode != null,
                EventType.Destroy => definition.CreateCode != null,
                EventType.Alarm => definition.AlarmScript.ContainsKey(eventSubtype),
                EventType.Step => definition.StepScript.ContainsKey((EventSubtypeStep)eventSubtype),
                EventType.Collision => definition.CollisionScript.ContainsKey(eventSubtype),
                EventType.Keyboard => definition.KeyboardScripts.ContainsKey((EventSubtypeKey)eventSubtype),
                EventType.Other => definition.OtherScript.ContainsKey((EventSubtypeOther)eventSubtype),
                EventType.Draw => definition.DrawScript.ContainsKey((EventSubtypeDraw)eventSubtype),
                EventType.KeyPress => definition.KeyPressScripts.ContainsKey((EventSubtypeKey)eventSubtype),
                EventType.KeyRelease => definition.KeyReleaseScripts.ContainsKey((EventSubtypeKey)eventSubtype),
                EventType.CleanUp => definition.CleanUpScript != null,
                EventType.PreCreate => definition.CreateCode != null,
                _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
            };

            if (has)
            {
                return true;
            }
        }

        return false;
    }
}
