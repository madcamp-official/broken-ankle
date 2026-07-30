using System.Collections.Generic;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Monster
{
    /// <summary>
    /// Where a body of a given size can stand in one map, and the shortest way between two of
    /// those places.
    ///
    /// Steering on its own cannot solve a house. A monster in a room with something to chase in
    /// the corridor has a straight line that runs through a wall, and every local rule for getting
    /// past that wall — slide left, slide right, turn around — is a guess made by something that
    /// does not know where the doorway is. It walks the wall and stays in the room.
    ///
    /// So the shape of the map is worked out once, when it loads, and routes are read off it.
    /// Obstacles are grown by the body's radius before anything is marked, which turns "will this
    /// fit through" into "is this cell free" and means a route through a one-cell doorway is one
    /// the body can actually follow rather than one a zero-width line could.
    ///
    /// Built by rasterising the colliders rather than by asking physics cell by cell: the walls
    /// are axis-aligned boxes, so their bounds are exactly their shape, and tens of thousands of
    /// overlap queries at the top of a map load is a hitch that arithmetic does not have.
    /// </summary>
    public class NavGrid
    {
        /// <summary>
        /// Sampling step, in world units. It has to be finer than the tightest gap a body may pass
        /// through, or the one column of free cells inside a doorway can fall between samples and
        /// the doorway is bricked up as far as the search is concerned. A one-unit doorway leaves a
        /// body of radius 0.35 a band 0.3 wide, and samples 0.25 apart always land in a band that
        /// size.
        /// </summary>
        public const float DefaultCellSize = 0.25f;

        /// <summary>Refuses to build anything larger. A map this big is a misconfiguration.</summary>
        const int MaxCells = 1_000_000;

        /// <summary>How far from an asked-for point it will look for somewhere legal to stand.</summary>
        const float SnapRadius = 3f;

        static readonly float[] NeighbourCost =
        {
            1f, 1f, 1f, 1f,
            1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f,
        };

        static readonly int[] NeighbourX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] NeighbourY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        readonly bool[] _free;
        readonly int _width;
        readonly int _height;
        readonly float _cellSize;
        readonly Vector2 _origin;

        // Kept between searches and marked with a generation number rather than cleared. Clearing
        // three arrays the size of the map on every repath costs more than the search does.
        readonly float[] _cost;
        readonly int[] _cameFrom;
        readonly int[] _seen;
        readonly int[] _closed;
        readonly MinHeap _open;
        readonly List<int> _cells = new();
        int _generation;

        /// <summary>Cells across, for diagnostics.</summary>
        public int Width => _width;

        /// <summary>Cells down, for diagnostics.</summary>
        public int Height => _height;

        NavGrid(bool[] free, int width, int height, float cellSize, Vector2 origin)
        {
            _free = free;
            _width = width;
            _height = height;
            _cellSize = cellSize;
            _origin = origin;

            var count = width * height;
            _cost = new float[count];
            _cameFrom = new int[count];
            _seen = new int[count];
            _closed = new int[count];
            _open = new MinHeap(Mathf.Min(count, 1024));
        }

        #region Cache

        readonly struct Request
        {
            public readonly MapZone Zone;
            public readonly float Radius;
            public readonly float CellSize;
            public readonly int Mask;

            public Request(MapZone zone, float radius, float cellSize, int mask)
            {
                Zone = zone;
                Radius = radius;
                CellSize = cellSize;
                Mask = mask;
            }

            public bool Matches(Request other) =>
                Zone == other.Zone &&
                Mask == other.Mask &&
                Mathf.Approximately(Radius, other.Radius) &&
                Mathf.Approximately(CellSize, other.CellSize);
        }

        static readonly List<(Request request, NavGrid grid)> _cache = new();

        /// <summary>
        /// The grid for this map and this body size, building it on first use. Shared: every
        /// monster of the same size in the same map reads the same one.
        /// </summary>
        public static NavGrid For(MapZone zone, float agentRadius, LayerMask obstacles,
                                  float cellSize = DefaultCellSize)
        {
            if (zone == null)
                return null;

            var wanted = new Request(zone, agentRadius, cellSize, obstacles.value);

            for (var i = _cache.Count - 1; i >= 0; i--)
            {
                // A map that has been unloaded takes its grid with it. Slots are reused, so a stale
                // entry would hand out the shape of a map that is no longer there.
                if (_cache[i].request.Zone == null)
                {
                    _cache.RemoveAt(i);
                    continue;
                }

                if (_cache[i].request.Matches(wanted))
                    return _cache[i].grid;
            }

            var grid = Build(zone, agentRadius, cellSize, obstacles);
            if (grid != null)
                _cache.Add((wanted, grid));

            return grid;
        }

        // Statics outlive a play session when the editor skips its domain reload, and a grid built
        // from the last run describes a map that no longer exists.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _cache.Clear();

        #endregion

        #region Building

        static NavGrid Build(MapZone zone, float agentRadius, float cellSize, LayerMask obstacles)
        {
            var solids = new List<Collider2D>();
            var bounds = new Bounds();
            var any = false;

            // Active objects only. A collider on something switched off is not stopping anybody,
            // and its bounds are not meaningful enough to be worth guessing at.
            foreach (var collider in zone.GetComponentsInChildren<Collider2D>(false))
            {
                if (!IsWorldGeometry(collider, obstacles))
                    continue;

                solids.Add(collider);

                if (any)
                {
                    bounds.Encapsulate(collider.bounds);
                }
                else
                {
                    bounds = collider.bounds;
                    any = true;
                }
            }

            if (!any)
            {
                Debug.LogWarning($"Map '{zone.Id}' has nothing solid in the obstacle layers, so " +
                                 "there is no shape to navigate. Anything hunting in it will walk " +
                                 "in straight lines.", zone);
                return null;
            }

            // Room enough outside the outer wall for the ring of blocked cells the wall itself
            // casts, so nothing legal ends up hard against the edge of the array.
            var padding = agentRadius + cellSize * 2f;
            var min = (Vector2)bounds.min - Vector2.one * padding;
            var max = (Vector2)bounds.max + Vector2.one * padding;

            var width = Mathf.CeilToInt((max.x - min.x) / cellSize) + 1;
            var height = Mathf.CeilToInt((max.y - min.y) / cellSize) + 1;

            if ((long)width * height > MaxCells)
            {
                Debug.LogError($"Map '{zone.Id}' would need {(long)width * height} navigation " +
                               $"cells at {cellSize} units each. Something in it is far from the " +
                               "rest of the map, or the cell size is far too small.", zone);
                return null;
            }

            var free = new bool[width * height];
            for (var i = 0; i < free.Length; i++)
                free[i] = true;

            var grid = new NavGrid(free, width, height, cellSize, min);
            foreach (var collider in solids)
                grid.Carve(collider.bounds, agentRadius);

            return grid;
        }

        /// <summary>
        /// Whether this collider is part of the map's shape.
        ///
        /// Triggers are not, and that matters more than it sounds: the room volumes the greybox
        /// builder lays down are triggers covering entire rooms, on the same layer as the walls.
        /// Anything that treats them as solid decides the whole room is a wall.
        ///
        /// Neither is anything with a moving body. A character standing still while the map loads
        /// would otherwise be baked into the map as a pillar and stay there after it walked off.
        /// </summary>
        static bool IsWorldGeometry(Collider2D collider, LayerMask obstacles)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
                return false;

            if ((obstacles.value & (1 << collider.gameObject.layer)) == 0)
                return false;

            var body = collider.attachedRigidbody;
            return body == null || body.bodyType == RigidbodyType2D.Static;
        }

        /// <summary>
        /// Blocks every cell a body of this radius could not stand in without touching the box.
        ///
        /// The box is grown by the radius and cells are tested by their centres, which is exact
        /// against a flat face and slightly generous at a corner. Generous is the safe direction:
        /// it can cost a route that only just existed, never invent one that does not.
        /// </summary>
        void Carve(Bounds box, float agentRadius)
        {
            var x0 = Mathf.CeilToInt((box.min.x - agentRadius - _origin.x) / _cellSize);
            var x1 = Mathf.FloorToInt((box.max.x + agentRadius - _origin.x) / _cellSize);
            var y0 = Mathf.CeilToInt((box.min.y - agentRadius - _origin.y) / _cellSize);
            var y1 = Mathf.FloorToInt((box.max.y + agentRadius - _origin.y) / _cellSize);

            x0 = Mathf.Max(x0, 0);
            y0 = Mathf.Max(y0, 0);
            x1 = Mathf.Min(x1, _width - 1);
            y1 = Mathf.Min(y1, _height - 1);

            for (var y = y0; y <= y1; y++)
            {
                var row = y * _width;
                for (var x = x0; x <= x1; x++)
                    _free[row + x] = false;
            }
        }

        #endregion

        #region Queries

        /// <summary>Whether a body of the radius this grid was built for fits here.</summary>
        public bool IsFree(Vector2 world)
        {
            var x = Mathf.RoundToInt((world.x - _origin.x) / _cellSize);
            var y = Mathf.RoundToInt((world.y - _origin.y) / _cellSize);
            return Contains(x, y) && _free[y * _width + x];
        }

        /// <summary>
        /// The nearest place to <paramref name="world"/> a body could stand.
        ///
        /// Needed at both ends of every search. A monster pressed against a wall is inside the
        /// band its own radius blocks off, and a sound heard from a player standing in a doorway
        /// lands somewhere no body fits. Refusing to search from or to those points would mean the
        /// monster stops working at exactly the moments it matters.
        /// </summary>
        public bool TryNearestFree(Vector2 world, out Vector2 result, float searchRadius = SnapRadius)
        {
            var cx = Mathf.RoundToInt((world.x - _origin.x) / _cellSize);
            var cy = Mathf.RoundToInt((world.y - _origin.y) / _cellSize);

            if (Contains(cx, cy) && _free[cy * _width + cx])
            {
                result = world;
                return true;
            }

            var rings = Mathf.CeilToInt(searchRadius / _cellSize);
            for (var ring = 1; ring <= rings; ring++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    var edge = Mathf.Abs(dy) == ring;
                    for (var dx = -ring; dx <= ring; dx += edge ? 1 : 2 * ring)
                    {
                        var x = cx + dx;
                        var y = cy + dy;
                        if (!Contains(x, y) || !_free[y * _width + x])
                            continue;

                        result = CentreOf(x, y);
                        return true;
                    }
                }
            }

            result = world;
            return false;
        }

        /// <summary>
        /// Fills <paramref name="path"/> with waypoints from <paramref name="from"/> to
        /// <paramref name="to"/>, or returns false if there is no way through.
        ///
        /// The waypoints are corners, not cells: a straight run across a room is two points rather
        /// than sixty, which is what keeps the monster walking in lines instead of a staircase.
        /// </summary>
        public bool TryFindPath(Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();

            if (!TryNearestFree(from, out var start) || !TryNearestFree(to, out var goal))
                return false;

            var startCell = IndexOf(start);
            var goalCell = IndexOf(goal);
            if (startCell < 0 || goalCell < 0)
                return false;

            if (startCell == goalCell)
            {
                path.Add(goal);
                return true;
            }

            if (!Search(startCell, goalCell))
                return false;

            Retrace(startCell, goalCell);
            Smooth(path);

            // The search works in cell centres; the caller asked for a point. Ending on the real
            // one saves a repath the moment the monster gets there.
            if (path.Count > 0 && IsFree(to))
                path[path.Count - 1] = to;

            return path.Count > 0;
        }

        bool Search(int startCell, int goalCell)
        {
            _generation++;
            _open.Clear();

            _cost[startCell] = 0f;
            _seen[startCell] = _generation;
            _cameFrom[startCell] = -1;
            _open.Push(startCell, Heuristic(startCell, goalCell));

            var goalX = goalCell % _width;
            var goalY = goalCell / _width;

            while (_open.TryPop(out var current))
            {
                if (_closed[current] == _generation)
                    continue;

                _closed[current] = _generation;

                if (current == goalCell)
                    return true;

                var cx = current % _width;
                var cy = current / _width;

                for (var n = 0; n < 8; n++)
                {
                    var nx = cx + NeighbourX[n];
                    var ny = cy + NeighbourY[n];

                    if (!Contains(nx, ny))
                        continue;

                    var neighbour = ny * _width + nx;
                    if (!_free[neighbour] || _closed[neighbour] == _generation)
                        continue;

                    // No cutting corners diagonally. The two cells either side have to be free as
                    // well, or the body clips the corner of a wall on the way past — which is the
                    // whole class of bug this grid exists to stop.
                    if (n >= 4 &&
                        (!_free[cy * _width + nx] || !_free[ny * _width + cx]))
                        continue;

                    var tentative = _cost[current] + NeighbourCost[n];
                    if (_seen[neighbour] == _generation && tentative >= _cost[neighbour])
                        continue;

                    _seen[neighbour] = _generation;
                    _cost[neighbour] = tentative;
                    _cameFrom[neighbour] = current;
                    _open.Push(neighbour, tentative + Octile(nx, ny, goalX, goalY));
                }
            }

            return false;
        }

        /// <summary>
        /// Walks the parents back from the goal. Keeps the starting cell: the smoothing that
        /// follows measures shortcuts from where the monster is standing.
        /// </summary>
        void Retrace(int startCell, int goalCell)
        {
            _cells.Clear();

            for (var cell = goalCell; cell != -1; cell = _cameFrom[cell])
            {
                _cells.Add(cell);
                if (cell == startCell)
                    break;
            }

            _cells.Reverse();
        }

        /// <summary>
        /// Turns the run of cells into the few points that actually change direction.
        ///
        /// Collinear cells go first, which is most of them, and then segments are merged wherever
        /// the body could take the shortcut instead. Without this the monster walks a staircase
        /// across open floor, which reads as something confused rather than something hunting.
        ///
        /// The merge only looks a bounded distance ahead. The path is thrown away and rebuilt
        /// several times a second, so tidying its far end is work nobody ever sees.
        /// </summary>
        void Smooth(List<Vector2> path)
        {
            const int Lookahead = 12;

            if (_cells.Count == 0)
                return;

            var corners = new List<Vector2>(16);
            for (var i = 0; i < _cells.Count; i++)
                if (i == 0 || i == _cells.Count - 1 || !Collinear(i))
                    corners.Add(CentreOf(_cells[i] % _width, _cells[i] / _width));

            // Starts at corners[0] and never emits it: that is the cell the monster is already
            // standing in, and steering towards it would be a step backwards.
            var at = 0;
            while (at < corners.Count - 1)
            {
                var next = at + 1;
                var limit = Mathf.Min(corners.Count - 1, at + Lookahead);

                for (var j = limit; j > at + 1; j--)
                {
                    if (!IsClear(corners[at], corners[j]))
                        continue;

                    next = j;
                    break;
                }

                path.Add(corners[next]);
                at = next;
            }
        }

        /// <summary>Whether the step into this cell and the step out of it point the same way.</summary>
        bool Collinear(int i)
        {
            var previous = _cells[i - 1];
            var cell = _cells[i];
            var next = _cells[i + 1];

            return cell % _width - previous % _width == next % _width - cell % _width &&
                   cell / _width - previous / _width == next / _width - cell / _width;
        }

        /// <summary>Whether a body could walk straight between two points.</summary>
        public bool IsClear(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance < 1e-4f)
                return true;

            var steps = Mathf.CeilToInt(distance / (_cellSize * 0.5f));
            for (var i = 1; i < steps; i++)
                if (!IsFree(from + delta * (i / (float)steps)))
                    return false;

            return IsFree(to);
        }

        bool Contains(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;

        Vector2 CentreOf(int x, int y) =>
            new(_origin.x + x * _cellSize, _origin.y + y * _cellSize);

        int IndexOf(Vector2 world)
        {
            var x = Mathf.RoundToInt((world.x - _origin.x) / _cellSize);
            var y = Mathf.RoundToInt((world.y - _origin.y) / _cellSize);
            return Contains(x, y) ? y * _width + x : -1;
        }

        float Heuristic(int cell, int goal) =>
            Octile(cell % _width, cell / _width, goal % _width, goal / _width);

        static float Octile(int x0, int y0, int x1, int y1)
        {
            var dx = Mathf.Abs(x1 - x0);
            var dy = Mathf.Abs(y1 - y0);
            return dx + dy + (1.41421356f - 2f) * Mathf.Min(dx, dy);
        }

        #endregion

        /// <summary>
        /// A binary heap of cells keyed by estimated total cost.
        ///
        /// A cell already on the heap is pushed again rather than moved, and whichever copy comes
        /// out first wins. Duplicates cost a little memory; supporting decrease-key costs an index
        /// per cell and the code to keep it honest.
        /// </summary>
        sealed class MinHeap
        {
            int[] _cells;
            float[] _keys;
            int _count;

            public MinHeap(int capacity)
            {
                _cells = new int[Mathf.Max(capacity, 8)];
                _keys = new float[_cells.Length];
            }

            public void Clear() => _count = 0;

            public void Push(int cell, float key)
            {
                if (_count == _cells.Length)
                {
                    System.Array.Resize(ref _cells, _count * 2);
                    System.Array.Resize(ref _keys, _count * 2);
                }

                var child = _count++;
                _cells[child] = cell;
                _keys[child] = key;

                while (child > 0)
                {
                    var parent = (child - 1) / 2;
                    if (_keys[parent] <= _keys[child])
                        break;

                    Swap(parent, child);
                    child = parent;
                }
            }

            public bool TryPop(out int cell)
            {
                if (_count == 0)
                {
                    cell = -1;
                    return false;
                }

                cell = _cells[0];
                _count--;
                _cells[0] = _cells[_count];
                _keys[0] = _keys[_count];

                var parent = 0;
                while (true)
                {
                    var left = parent * 2 + 1;
                    if (left >= _count)
                        break;

                    var smallest = left;
                    var right = left + 1;
                    if (right < _count && _keys[right] < _keys[left])
                        smallest = right;

                    if (_keys[parent] <= _keys[smallest])
                        break;

                    Swap(parent, smallest);
                    parent = smallest;
                }

                return true;
            }

            void Swap(int a, int b)
            {
                (_cells[a], _cells[b]) = (_cells[b], _cells[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
