using OpenGM.SerializedFiles;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// The motion planning (mp_*) stepping and path-generation functions, plus the mp_grid region
/// operations. Only mp_grid_create/_destroy/_add_cell/_clear_cell/_add_instances/_path/_draw
/// existed before; everything a game actually *steers* with was missing.
///
/// Ported from OpenGMK's <c>game/pathfinding.rs</c>, which is a direct reimplementation of GM8's
/// own routines and settles several things that aren't in the manual -- most importantly that
/// mp_potential_step probes a point <c>check_distance</c> steps ahead before committing to a move,
/// which is what stops an instance walking into a dead end it can see the far side of.
/// </summary>
public static class MotionPlanningFunctions
{
    /// <summary>
    /// mp_potential_settings is global rather than per instance, and these are GM's defaults.
    /// </summary>
    public static double PotentialMaxRotation = 30;
    public static double PotentialRotateStep = 10;
    public static double PotentialCheckDistance = 3;
    public static bool PotentialRotateOnSpot = true;

    public static void ResetPotentialSettings()
    {
        PotentialMaxRotation = 30;
        PotentialRotateStep = 10;
        PotentialCheckDistance = 3;
        PotentialRotateOnSpot = true;
    }

    /// <summary>True when <paramref name="self"/> would be colliding if it stood at (x, y).</summary>
    private static bool CollidesAt(GamemakerObject self, double x, double y, bool checkAll)
        => !CollisionManager.Command_PlaceFree(self, x, y, solidOnly: !checkAll);

    private static bool CollidesWithObjectAt(GamemakerObject self, double x, double y, int obj)
    {
        // GM's self (-1) can't collide with itself, so an object argument of self never blocks.
        if (obj == GMConstants.self)
        {
            return false;
        }

        return CollisionManager.Command_InstancePlace(self, x, y, obj) >= 0;
    }

    /*
     * Linear stepping
     */

    /// <summary>
    /// One straight step of at most <paramref name="stepSize"/> toward (x, y). Returns whether the
    /// goal was reached. A step that would collide is not taken at all -- the instance stays put and
    /// keeps its direction -- which is what makes mp_linear_step get stuck on walls rather than
    /// sliding along them.
    /// </summary>
    private static bool LinearStep(GamemakerObject self, double x, double y, double stepSize, Func<double, double, bool> collides)
    {
        var oldX = self.x;
        var oldY = self.y;

        if (oldX == x && oldY == y)
        {
            return true;
        }

        var xdist = x - oldX;
        var ydist = y - oldY;
        var distance = Math.Sqrt((xdist * xdist) + (ydist * ydist));

        var (newX, newY) = distance <= stepSize
            ? (x, y)
            : (oldX + (stepSize * xdist / distance), oldY + (stepSize * ydist / distance));

        if (!collides(newX, newY))
        {
            self.x = newX;
            self.y = newY;
            // Direction points at the goal, not at where the step ended -- they differ on the last,
            // short step into the target.
            self.direction = Math.Atan2(-ydist, xdist) * 180.0 / Math.PI;
        }

        // Reported even when the move was blocked: it answers "was the goal within one step", which
        // is what a caller loops on.
        return distance <= stepSize;
    }

    [GMLFunction("mp_linear_step")]
    public static object mp_linear_step(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var stepSize = args[2].Conv<double>();
        var checkAll = args[3].Conv<bool>();

        return LinearStep(self, x, y, stepSize, (px, py) => CollidesAt(self, px, py, checkAll));
    }

    [GMLFunction("mp_linear_step_object")]
    public static object mp_linear_step_object(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var stepSize = args[2].Conv<double>();
        var obj = args[3].Conv<int>();

        return LinearStep(self, x, y, stepSize, (px, py) => CollidesWithObjectAt(self, px, py, obj));
    }

    /*
     * Potential stepping -- a linear step that tries progressively wider angles when blocked.
     */

    private static bool PotentialStep(GamemakerObject self, double x, double y, double stepSize, Func<double, double, bool> collides)
    {
        var oldX = self.x;
        var oldY = self.y;

        if (oldX == x && oldY == y)
        {
            return true;
        }

        bool TryMove(double px, double py)
        {
            if (collides(px, py))
            {
                return false;
            }

            self.x = px;
            self.y = py;
            return true;
        }

        var distance = Math.Sqrt(((oldX - x) * (oldX - x)) + ((oldY - y) * (oldY - y)));
        var goalDirection = Mod360(Math.Atan2(oldY - y, x - oldX) * 180.0 / Math.PI);

        if (distance <= stepSize)
        {
            // Close enough to land on the goal in one step; no need to steer around anything.
            var reached = TryMove(x, y);
            if (reached)
            {
                self.direction = goalDirection;
            }
            return reached;
        }

        // Sweep outwards from straight-at-the-goal: 0, +step, -step, +2*step, -2*step, ... so the
        // least deviation that works wins. Stops at 180, i.e. once the sweep has covered every
        // direction.
        for (var offset = 0.0; offset < 180; offset += PotentialRotateStep)
        {
            foreach (var signed in offset == 0 ? new[] { 0.0 } : new[] { offset, -offset })
            {
                var direction = Mod360(goalDirection - signed);

                // Refuse anything that would turn further this step than max_rotation allows --
                // that limit is what gives potential movement its smooth arcs instead of
                // right-angle turns.
                var change = Mod360(direction - self.direction);
                if (change > PotentialMaxRotation && change < 360 - PotentialMaxRotation)
                {
                    continue;
                }

                var radians = direction * Math.PI / 180.0;
                var xStep = stepSize * Math.Cos(radians);
                var yStep = -stepSize * Math.Sin(radians);

                // Look check_distance steps ahead first. Without this an instance happily steps into
                // a pocket it cannot get out of, because the single step it is taking is clear.
                if (!collides(oldX + (xStep * PotentialCheckDistance), oldY + (yStep * PotentialCheckDistance))
                    && TryMove(oldX + xStep, oldY + yStep))
                {
                    self.direction = direction;
                    return true;
                }
            }
        }

        // Boxed in. Turning on the spot lets it try a different set of directions next step rather
        // than re-deriving the same blocked ones forever.
        if (PotentialRotateOnSpot)
        {
            self.direction += PotentialMaxRotation;
        }

        return false;
    }

    private static double Mod360(double degrees)
    {
        var result = degrees % 360;
        return result < 0 ? result + 360 : result;
    }

    [GMLFunction("mp_potential_step")]
    public static object mp_potential_step(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var stepSize = args[2].Conv<double>();
        var checkAll = args[3].Conv<bool>();

        return PotentialStep(self, x, y, stepSize, (px, py) => CollidesAt(self, px, py, checkAll));
    }

    [GMLFunction("mp_potential_step_object")]
    public static object mp_potential_step_object(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var x = args[0].Conv<double>();
        var y = args[1].Conv<double>();
        var stepSize = args[2].Conv<double>();
        var obj = args[3].Conv<int>();

        return PotentialStep(self, x, y, stepSize, (px, py) => CollidesWithObjectAt(self, px, py, obj));
    }

    [GMLFunction("mp_potential_settings")]
    public static object? mp_potential_settings(object?[] args)
    {
        PotentialMaxRotation = args[0].Conv<double>();
        PotentialRotateStep = args[1].Conv<double>();
        PotentialCheckDistance = args[2].Conv<double>();
        PotentialRotateOnSpot = args[3].Conv<bool>();
        return null;
    }

    /*
     * Path generation -- run the stepper to exhaustion and record where it went.
     */

    /// <summary>
    /// Walks <paramref name="step"/> until it reports success or stops making progress, writing the
    /// positions into <paramref name="path"/>, then puts the instance back exactly where it started.
    /// The restore is the whole contract: these functions plan a route, they don't walk it.
    /// </summary>
    private static bool MakePath(GamemakerObject self, CPath path, Func<bool?> step)
    {
        var oldX = self.x;
        var oldY = self.y;
        var oldDirection = self.direction;

        path.kind = 0;
        path.closed = false;
        PathManager.Clear(path);

        bool? result = null;
        // A hard cap so a stepper that inches forward forever can't hang the game. GM has no such
        // guard, but it also isn't running inside a browser frame budget.
        for (var i = 0; result == null && i < 10000; i++)
        {
            result = step();
            PathManager.AddPoint(path, self.x, self.y, 100);
        }

        self.x = oldX;
        self.y = oldY;
        self.direction = oldDirection;

        return result == true;
    }

    /// <summary>
    /// Wraps a stepper into the tri-state MakePath wants: null to keep going, true on arrival,
    /// false once a step stopped moving the instance at all (i.e. it's stuck).
    /// </summary>
    private static Func<bool?> UntilStuck(GamemakerObject self, Func<bool> step) => () =>
    {
        var beforeX = self.x;
        var beforeY = self.y;

        if (step())
        {
            return true;
        }

        return (self.x == beforeX && self.y == beforeY) ? false : null;
    };

    [GMLFunction("mp_linear_path")]
    public static object mp_linear_path(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var pathId = args[0].Conv<int>();
        var xg = args[1].Conv<double>();
        var yg = args[2].Conv<double>();
        var stepSize = args[3].Conv<double>();
        var checkAll = args[4].Conv<bool>();

        if (!PathManager.Paths.TryGetValue(pathId, out var path))
        {
            return false;
        }

        return MakePath(self, path, UntilStuck(self,
            () => LinearStep(self, xg, yg, stepSize, (px, py) => CollidesAt(self, px, py, checkAll))));
    }

    [GMLFunction("mp_linear_path_object")]
    public static object mp_linear_path_object(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var pathId = args[0].Conv<int>();
        var xg = args[1].Conv<double>();
        var yg = args[2].Conv<double>();
        var stepSize = args[3].Conv<double>();
        var obj = args[4].Conv<int>();

        if (!PathManager.Paths.TryGetValue(pathId, out var path))
        {
            return false;
        }

        return MakePath(self, path, UntilStuck(self,
            () => LinearStep(self, xg, yg, stepSize, (px, py) => CollidesWithObjectAt(self, px, py, obj))));
    }

    [GMLFunction("mp_potential_path")]
    public static object mp_potential_path(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var pathId = args[0].Conv<int>();
        var xg = args[1].Conv<double>();
        var yg = args[2].Conv<double>();
        var stepSize = args[3].Conv<double>();
        var factor = args[4].Conv<double>();
        var checkAll = args[5].Conv<bool>();

        return PotentialPath(self, pathId, xg, yg, stepSize, factor,
            (px, py) => CollidesAt(self, px, py, checkAll));
    }

    [GMLFunction("mp_potential_path_object")]
    public static object mp_potential_path_object(object?[] args)
    {
        var self = VMExecutor.Self.GMSelf;
        var pathId = args[0].Conv<int>();
        var xg = args[1].Conv<double>();
        var yg = args[2].Conv<double>();
        var stepSize = args[3].Conv<double>();
        var factor = args[4].Conv<double>();
        var obj = args[5].Conv<int>();

        return PotentialPath(self, pathId, xg, yg, stepSize, factor,
            (px, py) => CollidesWithObjectAt(self, px, py, obj));
    }

    private static bool PotentialPath(
        GamemakerObject self, int pathId, double xg, double yg, double stepSize, double factor,
        Func<double, double, bool> collides)
    {
        if (!PathManager.Paths.TryGetValue(pathId, out var path))
        {
            return false;
        }

        // `factor` (>= 1) is how much the potential field looks ahead while planning, expressed as a
        // multiple of the step size -- the same role check_distance plays for a live step. Swapped
        // in for the duration of the plan and put back afterwards, since the setting is global.
        var savedCheckDistance = PotentialCheckDistance;
        PotentialCheckDistance = Math.Max(1, factor);

        try
        {
            return MakePath(self, path, UntilStuck(self,
                () => PotentialStep(self, xg, yg, stepSize, collides)));
        }
        finally
        {
            PotentialCheckDistance = savedCheckDistance;
        }
    }

    /*
     * mp_grid region operations
     */

    private static void ForEachCellInRectangle(int id, object?[] args, Action<MPGrid, int, int> action)
    {
        if (!MotionPlanningManager.MPGrids.TryGetValue(id, out var grid))
        {
            return;
        }

        var left = args[1].Conv<double>();
        var top = args[2].Conv<double>();
        var right = args[3].Conv<double>();
        var bottom = args[4].Conv<double>();

        // The rectangle is in room coordinates; convert to cell indices and clamp. A cell counts as
        // covered if the rectangle touches it at all, which is why left/top floor and right/bottom
        // ceil-then-step-back rather than both rounding the same way.
        var h1 = (int)Math.Floor((left - grid.Left) / grid.CellWidth);
        var v1 = (int)Math.Floor((top - grid.Top) / grid.CellHeight);
        var h2 = (int)Math.Ceiling((right - grid.Left) / grid.CellWidth) - 1;
        var v2 = (int)Math.Ceiling((bottom - grid.Top) / grid.CellHeight) - 1;

        h1 = Math.Max(h1, 0);
        v1 = Math.Max(v1, 0);
        h2 = Math.Min(h2, grid.HCells - 1);
        v2 = Math.Min(v2, grid.VCells - 1);

        for (var h = h1; h <= h2; h++)
        {
            for (var v = v1; v <= v2; v++)
            {
                action(grid, h, v);
            }
        }
    }

    [GMLFunction("mp_grid_add_rectangle")]
    public static object? mp_grid_add_rectangle(object?[] args)
    {
        ForEachCellInRectangle(args[0].Conv<int>(), args, (grid, h, v) => grid.Poke(h, v, -1));
        return null;
    }

    [GMLFunction("mp_grid_clear_rectangle")]
    public static object? mp_grid_clear_rectangle(object?[] args)
    {
        ForEachCellInRectangle(args[0].Conv<int>(), args, (grid, h, v) => grid.Poke(h, v, 0));
        return null;
    }

    [GMLFunction("mp_grid_clear_all")]
    public static object? mp_grid_clear_all(object?[] args)
    {
        if (MotionPlanningManager.MPGrids.TryGetValue(args[0].Conv<int>(), out var grid))
        {
            Array.Clear(grid.Cells);
        }

        return null;
    }

    [GMLFunction("mp_grid_get_cell")]
    public static object mp_grid_get_cell(object?[] args)
    {
        if (!MotionPlanningManager.MPGrids.TryGetValue(args[0].Conv<int>(), out var grid))
        {
            return -1;
        }

        var h = args[1].Conv<int>();
        var v = args[2].Conv<int>();

        // Outside the grid reads as forbidden: an instance shouldn't be routed off the edge of the
        // world just because nothing marked it as blocked.
        if (h < 0 || h >= grid.HCells || v < 0 || v >= grid.VCells)
        {
            return -1;
        }

        return grid.Cells[(h * grid.VCells) + v];
    }
}
