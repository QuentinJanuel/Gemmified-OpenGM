namespace OpenGM;

/// <summary>
/// A transient, ad-hoc uniform spatial grid used to prune collision-candidate pairs whose
/// bounding boxes can't possibly overlap, before falling through to the existing precise
/// (AABB then per-pixel mask) test in <see cref="CollisionManager"/>. This is purely a
/// broad-phase performance optimization -- it never changes which pairs end up colliding, only
/// how many pairs need to be considered in the first place. See
/// <see cref="Rendering.DrawManager.UpdateCollisions"/> for the primary caller.
///
/// Ported from OpenGMK's gm8emulator/src/spatial_grid.rs (that fix: "Add a spatial-grid
/// broad-phase to run_collisions() to fix generic instance-count lag" -- the same
/// O(instances_of_A x instances_of_B) cost, without spatial pruning, that OpenGM's own
/// DrawManager.UpdateCollisions had).
///
/// Built fresh per call site (never stored on <see cref="GamemakerObject"/>), so it has no
/// savestate/serialization surface at all.
/// </summary>
public class SpatialGrid
{
    /// <summary>
    /// Cells are sized to at least this many pixels, so a room full of tiny particles doesn't
    /// produce a huge number of near-empty buckets.
    /// </summary>
    private const int MinCellSize = 64;

    private int _cellSize = MinCellSize;

    // Locked in by the first Insert and never changed afterward -- cell coordinates are
    // floor(position / cellSize), so mixing cell sizes across inserts/queries would put members
    // bucketed under one size into keys a query computed at a different size would never look
    // up, silently dropping them. A later, bigger bbox than the size was picked for just spans
    // more cells at the fixed size -- less efficient, still correct.
    private bool _cellSizeLocked;

    private readonly Dictionary<(int x, int y), List<GamemakerObject>> _buckets = new();
    private readonly Dictionary<GamemakerObject, List<(int x, int y)>> _membership = new();

    private static int FloorDiv(double value, int divisor) => (int)Math.Floor(value / divisor);

    private void EnsureCellSize(BBox bbox)
    {
        if (_cellSizeLocked)
        {
            return;
        }

        var dim = Math.Max(bbox.right - bbox.left + 1, bbox.bottom - bbox.top + 1);
        _cellSize = (int)Math.Max(dim, MinCellSize);
        _cellSizeLocked = true;
    }

    private IEnumerable<(int x, int y)> CellsFor(BBox bbox)
    {
        var cx0 = FloorDiv(bbox.left, _cellSize);
        var cy0 = FloorDiv(bbox.top, _cellSize);
        var cx1 = FloorDiv(bbox.right, _cellSize);
        var cy1 = FloorDiv(bbox.bottom, _cellSize);

        for (var cy = cy0; cy <= cy1; cy++)
        {
            for (var cx = cx0; cx <= cx1; cx++)
            {
                yield return (cx, cy);
            }
        }
    }

    public bool Contains(GamemakerObject obj) => _membership.ContainsKey(obj);

    public void Insert(GamemakerObject obj, BBox bbox)
    {
        EnsureCellSize(bbox);
        var cells = CellsFor(bbox).ToList();

        foreach (var cell in cells)
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
            {
                bucket = new List<GamemakerObject>();
                _buckets[cell] = bucket;
            }

            bucket.Add(obj);
        }

        _membership[obj] = cells;
    }

    public void Remove(GamemakerObject obj)
    {
        if (!_membership.TryGetValue(obj, out var cells))
        {
            return;
        }

        foreach (var cell in cells)
        {
            if (_buckets.TryGetValue(cell, out var bucket))
            {
                bucket.Remove(obj);
            }
        }

        _membership.Remove(obj);
    }

    /// <summary>Re-buckets a member that moved, e.g. after a solid collision's revert-to-previous-position.</summary>
    public void Reinsert(GamemakerObject obj, BBox newBbox)
    {
        Remove(obj);
        Insert(obj, newBbox);
    }

    /// <summary>
    /// Returns every distinct member whose bbox was inserted overlapping any cell <paramref name="bbox"/>
    /// also overlaps, sorted by ascending instanceId -- matching the order
    /// <see cref="InstanceManager.FindByAssetId"/>'s own explicit sort produces, so swapping a full
    /// scan for a grid query doesn't change which instance a caller sees first.
    /// </summary>
    public List<GamemakerObject> Query(BBox bbox)
    {
        var found = new HashSet<GamemakerObject>();

        foreach (var cell in CellsFor(bbox))
        {
            if (_buckets.TryGetValue(cell, out var bucket))
            {
                foreach (var obj in bucket)
                {
                    found.Add(obj);
                }
            }
        }

        var result = found.ToList();
        result.Sort((a, b) => a.instanceId.CompareTo(b.instanceId));
        return result;
    }
}
