using OpenGM.IO;
using System.Text;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    /// <summary>
    /// The ds_grid family. Split out of <see cref="DataStructuresFunctions"/> only for size -- the
    /// grid store lives here but ds_exists and Clear() over in the main file reach it as usual.
    ///
    /// Ported from the GameMaker HTML5 runtime's scripts/functions/collections/ds_grid.js. The
    /// conventions worth knowing, because they're not all obvious: regions are *inclusive* on both
    /// corners, get flipped if given backwards, and are clamped to the grid rather than erroring;
    /// "disk" variants take a centre and radius and use &lt;= on the squared distance; and add/multiply
    /// concatenate rather than error when both sides are strings.
    /// </summary>
    public static partial class DataStructuresFunctions
    {
        private class DsGrid(int width, int height)
        {
            public int Width = width;
            public int Height = height;
            public object?[] Cells = NewCells(width, height);

            public static object?[] NewCells(int width, int height)
            {
                var cells = new object?[width * height];
                Array.Fill(cells, 0);
                return cells;
            }

            public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

            public object? Get(int x, int y) => Cells[x + (y * Width)];

            public void Set(int x, int y, object? value) => Cells[x + (y * Width)] = value;
        }

        private static readonly Dictionary<int, DsGrid> _dsGridDict = new();

        private static DsGrid? GetGrid(object? id)
        {
            var index = id.Conv<int>();

            if (!_dsGridDict.TryGetValue(index, out var grid))
            {
                DebugLog.LogError($"Invalid ds_grid id {index}");
                return null;
            }

            return grid;
        }

        [GMLFunction("ds_grid_create")]
        public static object ds_grid_create(object?[] args)
        {
            var width = args[0].Conv<int>();
            var height = args[1].Conv<int>();

            if (width < 0 || height < 0)
            {
                DebugLog.LogError($"Invalid ds_grid size ({width},{height})");
                return -1;
            }

            var id = _dsGridDict.Count > 0 ? _dsGridDict.Keys.Max() + 1 : 0;
            _dsGridDict.Add(id, new DsGrid(width, height));
            return id;
        }

        [GMLFunction("ds_grid_destroy")]
        public static object? ds_grid_destroy(object?[] args)
        {
            _dsGridDict.Remove(args[0].Conv<int>());
            return null;
        }

        [GMLFunction("ds_grid_width")]
        public static object ds_grid_width(object?[] args) => GetGrid(args[0])?.Width ?? 0;

        [GMLFunction("ds_grid_height")]
        public static object ds_grid_height(object?[] args) => GetGrid(args[0])?.Height ?? 0;

        [GMLFunction("ds_grid_clear")]
        public static object? ds_grid_clear(object?[] args)
        {
            var grid = GetGrid(args[0]);
            if (grid != null)
            {
                Array.Fill(grid.Cells, args[1]);
            }

            return null;
        }

        [GMLFunction("ds_grid_copy")]
        public static object? ds_grid_copy(object?[] args)
        {
            var destination = GetGrid(args[0]);
            var source = GetGrid(args[1]);

            if (destination == null || source == null)
            {
                return null;
            }

            destination.Width = source.Width;
            destination.Height = source.Height;
            destination.Cells = (object?[])source.Cells.Clone();
            return null;
        }

        [GMLFunction("ds_grid_resize")]
        public static object? ds_grid_resize(object?[] args)
        {
            var grid = GetGrid(args[0]);
            var width = args[1].Conv<int>();
            var height = args[2].Conv<int>();

            if (grid == null || width < 0 || height < 0)
            {
                return null;
            }

            // Whatever still fits carries over; anything newly exposed starts at 0.
            var resized = DsGrid.NewCells(width, height);

            for (var y = 0; y < Math.Min(height, grid.Height); y++)
            {
                for (var x = 0; x < Math.Min(width, grid.Width); x++)
                {
                    resized[x + (y * width)] = grid.Get(x, y);
                }
            }

            grid.Width = width;
            grid.Height = height;
            grid.Cells = resized;
            return null;
        }

        [GMLFunction("ds_grid_set")]
        public static object? ds_grid_set(object?[] args)
        {
            var grid = GetGrid(args[0]);
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();

            if (grid != null && grid.InBounds(x, y))
            {
                grid.Set(x, y, args[3]);
            }

            return null;
        }

        [GMLFunction("ds_grid_get")]
        public static object? ds_grid_get(object?[] args)
        {
            var grid = GetGrid(args[0]);
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();

            return grid != null && grid.InBounds(x, y) ? grid.Get(x, y) : 0;
        }

        [GMLFunction("ds_grid_add")]
        public static object? ds_grid_add(object?[] args) => CombineCell(args, Add);

        [GMLFunction("ds_grid_multiply")]
        public static object? ds_grid_multiply(object?[] args) => CombineCell(args, Multiply);

        private static object? CombineCell(object?[] args, Func<object?, object?, object?> combine)
        {
            var grid = GetGrid(args[0]);
            var x = args[1].Conv<int>();
            var y = args[2].Conv<int>();

            if (grid != null && grid.InBounds(x, y))
            {
                grid.Set(x, y, combine(grid.Get(x, y), args[3]));
            }

            return null;
        }

        [GMLFunction("ds_grid_set_region")]
        public static object? ds_grid_set_region(object?[] args) => CombineRegion(args, Replace);

        [GMLFunction("ds_grid_add_region")]
        public static object? ds_grid_add_region(object?[] args) => CombineRegion(args, Add);

        [GMLFunction("ds_grid_multiply_region")]
        public static object? ds_grid_multiply_region(object?[] args) => CombineRegion(args, Multiply);

        private static object? CombineRegion(object?[] args, Func<object?, object?, object?> combine)
        {
            var grid = GetGrid(args[0]);
            if (grid == null)
            {
                return null;
            }

            var value = args[5];

            foreach (var (x, y) in Region(grid, args[1], args[2], args[3], args[4]))
            {
                grid.Set(x, y, combine(grid.Get(x, y), value));
            }

            return null;
        }

        [GMLFunction("ds_grid_set_disk")]
        public static object? ds_grid_set_disk(object?[] args) => CombineDisk(args, Replace);

        [GMLFunction("ds_grid_add_disk")]
        public static object? ds_grid_add_disk(object?[] args) => CombineDisk(args, Add);

        [GMLFunction("ds_grid_multiply_disk")]
        public static object? ds_grid_multiply_disk(object?[] args) => CombineDisk(args, Multiply);

        private static object? CombineDisk(object?[] args, Func<object?, object?, object?> combine)
        {
            var grid = GetGrid(args[0]);
            if (grid == null)
            {
                return null;
            }

            var value = args[4];

            foreach (var (x, y) in Disk(grid, args[1], args[2], args[3]))
            {
                grid.Set(x, y, combine(grid.Get(x, y), value));
            }

            return null;
        }

        [GMLFunction("ds_grid_set_grid_region")]
        public static object? ds_grid_set_grid_region(object?[] args) => CombineGridRegion(args, Replace);

        [GMLFunction("ds_grid_add_grid_region")]
        public static object? ds_grid_add_grid_region(object?[] args) => CombineGridRegion(args, Add);

        [GMLFunction("ds_grid_multiply_grid_region")]
        public static object? ds_grid_multiply_grid_region(object?[] args) => CombineGridRegion(args, Multiply);

        private static object? CombineGridRegion(object?[] args, Func<object?, object?, object?> combine)
        {
            var destination = GetGrid(args[0]);
            var source = GetGrid(args[1]);

            if (destination == null || source == null)
            {
                return null;
            }

            var xpos = args[6].Conv<int>();
            var ypos = args[7].Conv<int>();

            // The region is read from the source, so it's clamped against the source's bounds, then
            // offset into the destination -- cells landing outside the destination are dropped.
            var x1 = Math.Min(args[2].Conv<int>(), args[4].Conv<int>());
            var y1 = Math.Min(args[3].Conv<int>(), args[5].Conv<int>());

            foreach (var (x, y) in Region(source, args[2], args[3], args[4], args[5]))
            {
                var destX = xpos + (x - x1);
                var destY = ypos + (y - y1);

                if (destination.InBounds(destX, destY))
                {
                    destination.Set(destX, destY, combine(destination.Get(destX, destY), source.Get(x, y)));
                }
            }

            return null;
        }

        [GMLFunction("ds_grid_get_sum")]
        public static object ds_grid_get_sum(object?[] args)
            => Summarize(GetGrid(args[0]), Region(GetGrid(args[0]), args[1], args[2], args[3], args[4])).Sum;

        [GMLFunction("ds_grid_get_max")]
        public static object ds_grid_get_max(object?[] args)
            => Summarize(GetGrid(args[0]), Region(GetGrid(args[0]), args[1], args[2], args[3], args[4])).Max;

        [GMLFunction("ds_grid_get_min")]
        public static object ds_grid_get_min(object?[] args)
            => Summarize(GetGrid(args[0]), Region(GetGrid(args[0]), args[1], args[2], args[3], args[4])).Min;

        [GMLFunction("ds_grid_get_mean")]
        public static object ds_grid_get_mean(object?[] args)
            => Summarize(GetGrid(args[0]), Region(GetGrid(args[0]), args[1], args[2], args[3], args[4])).Mean;

        [GMLFunction("ds_grid_get_disk_sum")]
        public static object ds_grid_get_disk_sum(object?[] args)
            => Summarize(GetGrid(args[0]), Disk(GetGrid(args[0]), args[1], args[2], args[3])).Sum;

        [GMLFunction("ds_grid_get_disk_max")]
        public static object ds_grid_get_disk_max(object?[] args)
            => Summarize(GetGrid(args[0]), Disk(GetGrid(args[0]), args[1], args[2], args[3])).Max;

        [GMLFunction("ds_grid_get_disk_min")]
        public static object ds_grid_get_disk_min(object?[] args)
            => Summarize(GetGrid(args[0]), Disk(GetGrid(args[0]), args[1], args[2], args[3])).Min;

        [GMLFunction("ds_grid_get_disk_mean")]
        public static object ds_grid_get_disk_mean(object?[] args)
            => Summarize(GetGrid(args[0]), Disk(GetGrid(args[0]), args[1], args[2], args[3])).Mean;

        [GMLFunction("ds_grid_value_exists")]
        public static object ds_grid_value_exists(object?[] args)
            => FindValue(args, out _, out _);

        [GMLFunction("ds_grid_value_x")]
        public static object ds_grid_value_x(object?[] args)
        {
            FindValue(args, out var x, out _);
            return x;
        }

        [GMLFunction("ds_grid_value_y")]
        public static object ds_grid_value_y(object?[] args)
        {
            FindValue(args, out _, out var y);
            return y;
        }

        [GMLFunction("ds_grid_value_disk_exists")]
        public static object ds_grid_value_disk_exists(object?[] args)
            => FindValueDisk(args, out _, out _);

        [GMLFunction("ds_grid_value_disk_x")]
        public static object ds_grid_value_disk_x(object?[] args)
        {
            FindValueDisk(args, out var x, out _);
            return x;
        }

        [GMLFunction("ds_grid_value_disk_y")]
        public static object ds_grid_value_disk_y(object?[] args)
        {
            FindValueDisk(args, out _, out var y);
            return y;
        }

        private static bool FindValue(object?[] args, out int foundX, out int foundY)
        {
            var grid = GetGrid(args[0]);
            return Find(grid, Region(grid, args[1], args[2], args[3], args[4]), args[5], out foundX, out foundY);
        }

        private static bool FindValueDisk(object?[] args, out int foundX, out int foundY)
        {
            var grid = GetGrid(args[0]);
            return Find(grid, Disk(grid, args[1], args[2], args[3]), args[4], out foundX, out foundY);
        }

        private static bool Find(
            DsGrid? grid,
            IEnumerable<(int X, int Y)> cells,
            object? value,
            out int foundX,
            out int foundY)
        {
            if (grid != null)
            {
                foreach (var (x, y) in cells)
                {
                    if (ValuesEqual(grid.Get(x, y), value))
                    {
                        foundX = x;
                        foundY = y;
                        return true;
                    }
                }
            }

            foundX = -1;
            foundY = -1;
            return false;
        }

        [GMLFunction("ds_grid_shuffle")]
        public static object? ds_grid_shuffle(object?[] args)
        {
            var grid = GetGrid(args[0]);
            if (grid == null)
            {
                return null;
            }

            for (var i = grid.Cells.Length - 1; i > 0; i--)
            {
                var j = (int)GMRandom.YYRandom(i + 1);
                (grid.Cells[i], grid.Cells[j]) = (grid.Cells[j], grid.Cells[i]);
            }

            return null;
        }

        [GMLFunction("ds_grid_sort")]
        public static object? ds_grid_sort(object?[] args)
        {
            var grid = GetGrid(args[0]);
            var column = args[1].Conv<int>();
            var ascending = args[2].Conv<bool>();

            if (grid == null || column < 0 || column >= grid.Width)
            {
                return null;
            }

            // Sorting reorders whole rows by one column's values, so it's a stable sort of the row
            // indices followed by a rebuild rather than a sort of the backing array.
            var rows = Enumerable.Range(0, grid.Height)
                .OrderBy(y => grid.Get(column, y), GMValueComparer.Instance)
                .ToList();

            if (!ascending)
            {
                rows.Reverse();
            }

            var sorted = DsGrid.NewCells(grid.Width, grid.Height);

            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    sorted[x + (y * grid.Width)] = grid.Get(x, rows[y]);
                }
            }

            grid.Cells = sorted;
            return null;
        }

        [GMLFunction("ds_grid_write")]
        public static object? ds_grid_write(object?[] args)
        {
            var grid = GetGrid(args[0]);
            if (grid == null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(603);
            writer.Write(grid.Width);
            writer.Write(grid.Height);

            // Column-major, matching the runtime's own ordering so a written grid reads back the
            // same way round.
            for (var x = 0; x < grid.Width; x++)
            {
                for (var y = 0; y < grid.Height; y++)
                {
                    WriteDsValue(writer, grid.Get(x, y));
                }
            }

            writer.Flush();
            return BitConverter.ToString(stream.ToArray()).Replace("-", "");
        }

        [GMLFunction("ds_grid_read")]
        public static object? ds_grid_read(object?[] args)
        {
            var grid = GetGrid(args[0]);
            if (grid == null)
            {
                return null;
            }

            using var stream = new MemoryStream(Convert.FromHexString(args[1].Conv<string>()));
            using var reader = new BinaryReader(stream);

            reader.ReadInt32(); // header
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();

            grid.Width = width;
            grid.Height = height;
            grid.Cells = DsGrid.NewCells(width, height);

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    grid.Set(x, y, ReadDsValue(reader));
                }
            }

            return null;
        }

        /// <summary>
        /// The cells of an inclusive region, flipped if given backwards and clamped to the grid --
        /// GML code passes regions bigger than the grid freely and expects the overhang ignored, not
        /// an error.
        /// </summary>
        private static IEnumerable<(int X, int Y)> Region(DsGrid? grid, object? x1, object? y1, object? x2, object? y2)
        {
            if (grid == null)
            {
                yield break;
            }

            var left = Math.Max(0, Math.Min(x1.Conv<int>(), x2.Conv<int>()));
            var right = Math.Min(grid.Width - 1, Math.Max(x1.Conv<int>(), x2.Conv<int>()));
            var top = Math.Max(0, Math.Min(y1.Conv<int>(), y2.Conv<int>()));
            var bottom = Math.Min(grid.Height - 1, Math.Max(y1.Conv<int>(), y2.Conv<int>()));

            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    yield return (x, y);
                }
            }
        }

        private static IEnumerable<(int X, int Y)> Disk(DsGrid? grid, object? xm, object? ym, object? radius)
        {
            if (grid == null)
            {
                yield break;
            }

            var centreX = xm.Conv<double>();
            var centreY = ym.Conv<double>();
            var r = radius.Conv<double>();
            var rSquared = r * r;

            var left = Math.Max(0, (int)Math.Floor(centreX - r));
            var right = Math.Min(grid.Width - 1, (int)Math.Ceiling(centreX + r));
            var top = Math.Max(0, (int)Math.Floor(centreY - r));
            var bottom = Math.Min(grid.Height - 1, (int)Math.Ceiling(centreY + r));

            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var dx = x - centreX;
                    var dy = y - centreY;

                    if ((dx * dx) + (dy * dy) <= rSquared)
                    {
                        yield return (x, y);
                    }
                }
            }
        }

        private static (double Sum, double Max, double Min, double Mean) Summarize(
            DsGrid? grid,
            IEnumerable<(int X, int Y)> cells)
        {
            if (grid == null)
            {
                return (0, 0, 0, 0);
            }

            double sum = 0, max = 0, min = 0, count = 0;
            var first = true;

            foreach (var (x, y) in cells)
            {
                var value = grid.Get(x, y);
                count++;

                // Strings count towards the cell count (and so drag the mean down) but contribute
                // nothing to the sum, matching the runtime.
                var number = value is string ? 0 : value.Conv<double>();

                if (first)
                {
                    max = min = number;
                    first = false;
                }
                else
                {
                    max = Math.Max(max, number);
                    min = Math.Min(min, number);
                }

                sum += number;
            }

            return (sum, max, min, count > 0 ? sum / count : 0);
        }

        private static object? Replace(object? existing, object? value) => value;

        private static object? Add(object? existing, object? value)
            => existing is string || value is string
                ? existing.Conv<string>() + value.Conv<string>()
                : existing.Conv<double>() + value.Conv<double>();

        private static object? Multiply(object? existing, object? value)
            => existing is string || value is string
                ? existing
                : existing.Conv<double>() * value.Conv<double>();

        private static bool ValuesEqual(object? a, object? b)
            => a is string || b is string
                ? a is string left && b is string right && left == right
                : a.Conv<double>() == b.Conv<double>();

    }
}
