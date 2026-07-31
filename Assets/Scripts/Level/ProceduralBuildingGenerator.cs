using System;
using System.Collections;
using System.Collections.Generic;
using GypsyAliens.Core;
using UnityEngine;

namespace GypsyAliens.Level
{
    /// <summary>
    /// Contiguous rooms on a shared grid. Shared walls with a clear 2-cell doorway.
    /// Builds a navigation graph and assigns Floor/Wall physics layers.
    /// </summary>
    public sealed class ProceduralBuildingGenerator : MonoBehaviour
    {
        const int DoorWidthCells = 2;

        [SerializeField] BuildingTileSet _tileSet;
        [SerializeField] Transform _levelRoot;
        [SerializeField] int _gridWidth = 4;
        [SerializeField] int _gridHeight = 4;
        [SerializeField] int _minRooms = 7;
        [SerializeField] int _maxRooms = 10;
        [SerializeField] int _roomSize = 10;
        [SerializeField] float _spawnHeight = 0.15f;
        [SerializeField] int _yieldEveryRooms = 1;
        [SerializeField, Range(0f, 1f)] float _propChancePerSlot = 0.63f;
        [SerializeField] float _propInsetCells = 0.85f;
        [SerializeField] float _propWallClearance = 0.12f;

        public Vector3 SpawnPosition { get; private set; }
        public bool HasSpawnPoint { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsGenerating { get; private set; }
        public LevelNavigationMap NavigationMap { get; private set; }

        public event Action GenerationStarted;
        public event Action LevelReady;

        public void SetTileSet(BuildingTileSet tileSet) => _tileSet = tileSet;

        public void SetLevelRoot(Transform levelRoot) => _levelRoot = levelRoot;

        public void Generate(int seed)
        {
            var enumerator = GenerateRoutine(seed);
            while (enumerator.MoveNext())
            {
            }
        }

        public IEnumerator GenerateRoutine(int seed)
        {
            IsReady = false;
            HasSpawnPoint = false;
            IsGenerating = true;
            NavigationMap = null;
            GenerationStarted?.Invoke();
            yield return null;

            if (_tileSet == null)
            {
                Debug.LogError("ProceduralBuildingGenerator: BuildingTileSet is missing.", this);
                IsGenerating = false;
                yield break;
            }

            if (_levelRoot == null)
            {
                _levelRoot = new GameObject("LevelRoot").transform;
            }

            ClearLevel();
            yield return null;

            var size = Mathf.Max(DoorWidthCells + 2, _roomSize);
            var rng = new System.Random(seed);
            var rooms = BuildRoomGraph(rng);
            var cell = _tileSet.CellSize;

            foreach (var room in rooms)
            {
                room.MinX = room.GridPos.x * size;
                room.MinZ = room.GridPos.y * size;
                room.Size = size;
            }

            var parent = new GameObject("Building").transform;
            parent.SetParent(_levelRoot, false);

            var built = 0;
            foreach (var room in rooms)
            {
                BuildFloors(room, parent, cell);
                built++;
                if (_yieldEveryRooms > 0 && built % _yieldEveryRooms == 0)
                {
                    yield return null;
                }
            }

            var roomMap = new Dictionary<Vector2Int, RoomData>();
            foreach (var room in rooms)
            {
                roomMap[room.GridPos] = room;
            }

            BuildWalls(rooms, roomMap, parent, cell);
            BuildRoomProps(rooms, roomMap, parent, cell, rng);
            NavigationMap = BuildNavigationMap(rooms, roomMap, cell);
            yield return null;

            if (rooms.Count > 0)
            {
                var first = rooms[0];
                SpawnPosition = new Vector3(
                    (first.MinX + first.Size * 0.5f) * cell,
                    _spawnHeight,
                    (first.MinZ + first.Size * 0.5f) * cell);
                HasSpawnPoint = true;
            }
            else
            {
                SpawnPosition = new Vector3(0f, _spawnHeight, 0f);
            }

            Physics.SyncTransforms();
            IsGenerating = false;
            IsReady = HasSpawnPoint;
            LevelReady?.Invoke();
        }

        void ClearLevel()
        {
            IsReady = false;
            HasSpawnPoint = false;
            NavigationMap = null;

            for (var i = _levelRoot.childCount - 1; i >= 0; i--)
            {
                var child = _levelRoot.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        List<RoomData> BuildRoomGraph(System.Random rng)
        {
            var targetRooms = Mathf.Clamp(rng.Next(_minRooms, _maxRooms + 1), 1, _gridWidth * _gridHeight);
            var map = new Dictionary<Vector2Int, RoomData>();
            var order = new List<RoomData>();
            var directions = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0),
                new Vector2Int(0, 1), new Vector2Int(0, -1),
            };

            order.Add(CreateRoom(Vector2Int.zero, map));

            while (order.Count < targetRooms)
            {
                var candidates = new List<(RoomData room, Vector2Int next, Vector2Int dir)>();
                foreach (var room in order)
                {
                    foreach (var dir in directions)
                    {
                        var next = room.GridPos + dir;
                        if (next.x < 0 || next.y < 0 || next.x >= _gridWidth || next.y >= _gridHeight)
                        {
                            continue;
                        }

                        if (map.ContainsKey(next))
                        {
                            continue;
                        }

                        candidates.Add((room, next, dir));
                    }
                }

                if (candidates.Count == 0)
                {
                    break;
                }

                var pick = candidates[rng.Next(candidates.Count)];
                var created = CreateRoom(pick.next, map);
                order.Add(created);
                pick.room.Connections.Add(pick.dir);
                created.Connections.Add(new Vector2Int(-pick.dir.x, -pick.dir.y));
            }

            // Assign stable ids.
            for (var i = 0; i < order.Count; i++)
            {
                order[i].Id = i;
            }

            return order;
        }

        static RoomData CreateRoom(Vector2Int cell, Dictionary<Vector2Int, RoomData> map)
        {
            var room = new RoomData(cell);
            map[cell] = room;
            return room;
        }

        void BuildFloors(RoomData room, Transform parent, float cell)
        {
            var roomRoot = new GameObject($"Room_{room.GridPos.x}_{room.GridPos.y}").transform;
            roomRoot.SetParent(parent, false);

            var floorLayer = GameLayers.Floor;
            for (var x = 0; x < room.Size; x++)
            {
                for (var z = 0; z < room.Size; z++)
                {
                    var world = new Vector3((room.MinX + x) * cell, 0f, (room.MinZ + z) * cell);
                    var floor = Spawn(_tileSet.FloorPrefab, roomRoot, world, Quaternion.identity);
                    EnsureWalkableCollider(floor, cell);
                    if (floor != null)
                    {
                        GameLayers.SetLayerRecursively(floor, floorLayer);
                    }
                }
            }
        }

        void BuildWalls(
            List<RoomData> rooms,
            Dictionary<Vector2Int, RoomData> map,
            Transform parent,
            float cell)
        {
            var wallsRoot = new GameObject("Walls").transform;
            wallsRoot.SetParent(parent, false);

            foreach (var room in rooms)
            {
                BuildEdge(room, map, wallsRoot, cell, Vector2Int.up);
                BuildEdge(room, map, wallsRoot, cell, Vector2Int.down);
                BuildEdge(room, map, wallsRoot, cell, Vector2Int.right);
                BuildEdge(room, map, wallsRoot, cell, Vector2Int.left);
            }
        }

        /// <summary>
        /// Places small props along each room's inner wall perimeter (skipping doorways).
        /// Uses Default layer (not Wall) so X-ray never fades them; colliders still block near walls.
        /// </summary>
        void BuildRoomProps(
            List<RoomData> rooms,
            Dictionary<Vector2Int, RoomData> map,
            Transform parent,
            float cell,
            System.Random rng)
        {
            var prefabs = _tileSet.RoomPropPrefabs;
            if (prefabs == null || prefabs.Length == 0)
            {
                return;
            }

            var valid = 0;
            for (var i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null)
                {
                    valid++;
                }
            }

            if (valid == 0)
            {
                return;
            }

            var propsRoot = new GameObject("RoomProps").transform;
            propsRoot.SetParent(parent, false);
            var inset = Mathf.Clamp(_propInsetCells, 0.35f, 2f) * cell;
            var chance = Mathf.Clamp01(_propChancePerSlot);
            var clearance = Mathf.Max(0.05f, _propWallClearance);

            foreach (var room in rooms)
            {
                PlacePropsAlongEdge(room, map, propsRoot, cell, inset, chance, clearance, prefabs, rng, Vector2Int.up);
                PlacePropsAlongEdge(room, map, propsRoot, cell, inset, chance, clearance, prefabs, rng, Vector2Int.down);
                PlacePropsAlongEdge(room, map, propsRoot, cell, inset, chance, clearance, prefabs, rng, Vector2Int.right);
                PlacePropsAlongEdge(room, map, propsRoot, cell, inset, chance, clearance, prefabs, rng, Vector2Int.left);
            }
        }

        void PlacePropsAlongEdge(
            RoomData room,
            Dictionary<Vector2Int, RoomData> map,
            Transform parent,
            float cell,
            float inset,
            float chance,
            float clearance,
            GameObject[] prefabs,
            System.Random rng,
            Vector2Int dir)
        {
            var size = room.Size;
            var hasNeighbor = map.ContainsKey(room.GridPos + dir);
            var doorStart = hasNeighbor ? (size - DoorWidthCells) / 2 : -1;
            // Skip corner cells so adjacent edges don't double-stack props.
            for (var i = 1; i < size - 1; i++)
            {
                if (doorStart >= 0 && i >= doorStart && i < doorStart + DoorWidthCells)
                {
                    continue;
                }

                // Also skip one cell beside doorways for clearance.
                if (doorStart >= 0
                    && (i == doorStart - 1 || i == doorStart + DoorWidthCells))
                {
                    continue;
                }

                if (rng.NextDouble() > chance)
                {
                    continue;
                }

                var prefab = prefabs[rng.Next(prefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                GetPerimeterPropPose(room, cell, inset, dir, i, out var pos, out var rot);
                var prop = Spawn(prefab, parent, pos, rot);
                if (prop == null)
                {
                    continue;
                }

                OrientPropFlatAgainstWall(prop, dir, rot);
                EnsurePropCollider(prop);
                SnapPropClearOfWall(prop, room, cell, dir, clearance);
            }
        }

        static void GetPerimeterPropPose(
            RoomData room,
            float cell,
            float inset,
            Vector2Int dir,
            int index,
            out Vector3 pos,
            out Quaternion rot)
        {
            var size = room.Size;
            // Wall faces outward; props sit just inside the room.
            if (dir.y > 0)
            {
                pos = new Vector3((room.MinX + index + 0.5f) * cell, 0f, (room.MinZ + size) * cell - inset);
                rot = Quaternion.identity;
            }
            else if (dir.y < 0)
            {
                pos = new Vector3((room.MinX + index + 0.5f) * cell, 0f, room.MinZ * cell + inset);
                rot = Quaternion.identity;
            }
            else if (dir.x > 0)
            {
                pos = new Vector3((room.MinX + size) * cell - inset, 0f, (room.MinZ + index + 0.5f) * cell);
                rot = Quaternion.identity;
            }
            else
            {
                pos = new Vector3(room.MinX * cell + inset, 0f, (room.MinZ + index + 0.5f) * cell);
                rot = Quaternion.identity;
            }
        }

        /// <summary>
        /// Picks a 90° yaw that minimizes depth into the room (thin side faces the wall).
        /// </summary>
        static void OrientPropFlatAgainstWall(GameObject prop, Vector2Int dir, Quaternion baseRot)
        {
            var t = prop.transform;
            var origin = t.position;
            var bestYaw = 0;
            var bestDepth = float.MaxValue;

            for (var yaw = 0; yaw < 4; yaw++)
            {
                t.SetPositionAndRotation(origin, baseRot * Quaternion.Euler(0f, yaw * 90f, 0f));
                var bounds = GetWorldBounds(prop);
                var depth = dir.x != 0 ? bounds.size.x : bounds.size.z;
                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    bestYaw = yaw;
                }
            }

            t.SetPositionAndRotation(origin, baseRot * Quaternion.Euler(0f, bestYaw * 90f, 0f));
        }

        /// <summary>
        /// Pushes the prop inward until its world bounds clear the wall plane by <paramref name="clearance"/>.
        /// </summary>
        static void SnapPropClearOfWall(
            GameObject prop,
            RoomData room,
            float cell,
            Vector2Int dir,
            float clearance)
        {
            var bounds = GetWorldBounds(prop);
            var t = prop.transform;
            var size = room.Size;

            if (dir.y > 0)
            {
                var wallZ = (room.MinZ + size) * cell;
                var overflow = bounds.max.z - (wallZ - clearance);
                if (overflow > 0f)
                {
                    t.position += new Vector3(0f, 0f, -overflow);
                }
            }
            else if (dir.y < 0)
            {
                var wallZ = room.MinZ * cell;
                var overflow = (wallZ + clearance) - bounds.min.z;
                if (overflow > 0f)
                {
                    t.position += new Vector3(0f, 0f, overflow);
                }
            }
            else if (dir.x > 0)
            {
                var wallX = (room.MinX + size) * cell;
                var overflow = bounds.max.x - (wallX - clearance);
                if (overflow > 0f)
                {
                    t.position += new Vector3(-overflow, 0f, 0f);
                }
            }
            else
            {
                var wallX = room.MinX * cell;
                var overflow = (wallX + clearance) - bounds.min.x;
                if (overflow > 0f)
                {
                    t.position += new Vector3(overflow, 0f, 0f);
                }
            }
        }

        static Bounds GetWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one * 0.5f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return bounds;
        }

        static void EnsurePropCollider(GameObject prop)
        {
            if (prop == null)
            {
                return;
            }

            if (prop.GetComponentInChildren<Collider>() != null)
            {
                return;
            }

            var renderer = prop.GetComponentInChildren<Renderer>();
            var box = prop.AddComponent<BoxCollider>();
            if (renderer != null)
            {
                var b = renderer.bounds;
                box.center = prop.transform.InverseTransformPoint(b.center);
                var lossy = prop.transform.lossyScale;
                box.size = new Vector3(
                    SafeDiv(b.size.x, lossy.x),
                    SafeDiv(b.size.y, lossy.y),
                    SafeDiv(b.size.z, lossy.z));
            }
            else
            {
                box.size = new Vector3(0.6f, 0.8f, 0.6f);
                box.center = new Vector3(0f, 0.4f, 0f);
            }
        }

        static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

        void BuildEdge(
            RoomData room,
            Dictionary<Vector2Int, RoomData> map,
            Transform parent,
            float cell,
            Vector2Int dir)
        {
            var neighborPos = room.GridPos + dir;
            var hasNeighbor = map.ContainsKey(neighborPos);
            if (hasNeighbor && !IsWallOwner(room.GridPos, neighborPos))
            {
                return;
            }

            var size = room.Size;
            var doorStart = hasNeighbor ? (size - DoorWidthCells) / 2 : -1;
            var alongX = dir.x == 0;
            var wallLayer = GameLayers.Wall;

            for (var i = 0; i < size; i++)
            {
                if (doorStart >= 0 && i >= doorStart && i < doorStart + DoorWidthCells)
                {
                    continue;
                }

                var wall = PlaceUnitWall(room, parent, cell, dir, alongX, i);
                if (wall != null)
                {
                    GameLayers.SetLayerRecursively(wall, wallLayer);
                }
            }

            if (doorStart >= 0 && _tileSet.DoorWallPrefab != null)
            {
                var door = PlaceDoorWall(room, parent, cell, dir, alongX, doorStart);
                if (door != null)
                {
                    // Same layer as walls for X-ray; colliders are triggers so they never block movement/clicks.
                    GameLayers.SetLayerRecursively(door, wallLayer);
                }
            }
        }

        LevelNavigationMap BuildNavigationMap(
            List<RoomData> rooms,
            Dictionary<Vector2Int, RoomData> map,
            float cell)
        {
            var nodes = new List<RoomNavNode>(rooms.Count);
            var byGrid = new Dictionary<Vector2Int, RoomNavNode>();

            foreach (var room in rooms)
            {
                var node = new RoomNavNode
                {
                    Id = room.Id,
                    GridPos = room.GridPos,
                    Bounds = new Rect(
                        room.MinX * cell,
                        room.MinZ * cell,
                        room.Size * cell,
                        room.Size * cell),
                    Center = new Vector3(
                        (room.MinX + room.Size * 0.5f) * cell,
                        _spawnHeight,
                        (room.MinZ + room.Size * 0.5f) * cell),
                };
                nodes.Add(node);
                byGrid[room.GridPos] = node;
            }

            foreach (var room in rooms)
            {
                var from = byGrid[room.GridPos];
                foreach (var dir in room.Connections)
                {
                    var neighborPos = room.GridPos + dir;
                    if (!byGrid.TryGetValue(neighborPos, out var to))
                    {
                        continue;
                    }

                    // Only author each door once from the wall owner side, but add links both ways.
                    if (!IsWallOwner(room.GridPos, neighborPos))
                    {
                        continue;
                    }

                    var doorPos = GetDoorWorldPosition(room, dir, cell);
                    from.Doors.Add(new RoomDoorLink
                    {
                        FromRoomId = from.Id,
                        ToRoomId = to.Id,
                        DoorPosition = doorPos,
                    });
                    to.Doors.Add(new RoomDoorLink
                    {
                        FromRoomId = to.Id,
                        ToRoomId = from.Id,
                        DoorPosition = doorPos,
                    });
                }
            }

            // Also link rooms that touch even if Connections missed (adjacent square fill).
            foreach (var room in rooms)
            {
                var from = byGrid[room.GridPos];
                foreach (var dir in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var neighborPos = room.GridPos + dir;
                    if (!byGrid.TryGetValue(neighborPos, out var to))
                    {
                        continue;
                    }

                    if (!IsWallOwner(room.GridPos, neighborPos))
                    {
                        continue;
                    }

                    if (HasDoorLink(from, to.Id))
                    {
                        continue;
                    }

                    var doorPos = GetDoorWorldPosition(room, dir, cell);
                    from.Doors.Add(new RoomDoorLink
                    {
                        FromRoomId = from.Id,
                        ToRoomId = to.Id,
                        DoorPosition = doorPos,
                    });
                    to.Doors.Add(new RoomDoorLink
                    {
                        FromRoomId = to.Id,
                        ToRoomId = from.Id,
                        DoorPosition = doorPos,
                    });
                }
            }

            return new LevelNavigationMap(nodes);
        }

        static bool HasDoorLink(RoomNavNode from, int toId)
        {
            foreach (var door in from.Doors)
            {
                if (door.ToRoomId == toId)
                {
                    return true;
                }
            }

            return false;
        }

        Vector3 GetDoorWorldPosition(RoomData room, Vector2Int dir, float cell)
        {
            var size = room.Size;
            var doorStart = (size - DoorWidthCells) / 2;
            var doorMid = doorStart + DoorWidthCells * 0.5f;

            if (dir.x == 0)
            {
                var z = dir.y > 0 ? room.MinZ + size : room.MinZ;
                return new Vector3((room.MinX + doorMid) * cell, _spawnHeight, z * cell);
            }

            var x = dir.x > 0 ? room.MinX + size : room.MinX;
            return new Vector3(x * cell, _spawnHeight, (room.MinZ + doorMid) * cell);
        }

        GameObject PlaceUnitWall(RoomData room, Transform parent, float cell, Vector2Int dir, bool alongX, int index)
        {
            GetEdgePose(room, cell, dir, alongX, index, out var pos, out var rot);
            return Spawn(_tileSet.WallPrefab, parent, pos, rot);
        }

        GameObject PlaceDoorWall(RoomData room, Transform parent, float cell, Vector2Int dir, bool alongX, int doorStart)
        {
            GetEdgePose(room, cell, dir, alongX, doorStart, out var pos, out var rot);
            var door = Spawn(_tileSet.DoorWallPrefab, parent, pos, rot);
            if (door == null)
            {
                return null;
            }

            // Non-convex MeshColliders cannot be triggers — replace with a trigger box for X-ray overlap.
            foreach (var col in door.GetComponentsInChildren<Collider>())
            {
                if (col == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(col);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(col);
                }
            }

            var renderer = door.GetComponentInChildren<Renderer>();
            var box = door.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (renderer != null)
            {
                var localBounds = GetLocalRendererBounds(door.transform, renderer);
                box.center = localBounds.center;
                box.size = localBounds.size;
            }

            return door;
        }

        static Bounds GetLocalRendererBounds(Transform root, Renderer renderer)
        {
            var world = renderer.bounds;
            var localCenter = root.InverseTransformPoint(world.center);
            var axisX = root.InverseTransformVector(new Vector3(world.size.x, 0f, 0f));
            var axisY = root.InverseTransformVector(new Vector3(0f, world.size.y, 0f));
            var axisZ = root.InverseTransformVector(new Vector3(0f, 0f, world.size.z));
            var localSize = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(localCenter, localSize);
        }

        static void GetEdgePose(
            RoomData room,
            float cell,
            Vector2Int dir,
            bool alongX,
            int index,
            out Vector3 pos,
            out Quaternion rot)
        {
            if (alongX)
            {
                var z = dir.y > 0 ? room.MinZ + room.Size : room.MinZ;
                pos = new Vector3((room.MinX + index) * cell, 0f, z * cell);
                rot = Quaternion.identity;
            }
            else
            {
                var x = dir.x > 0 ? room.MinX + room.Size : room.MinX;
                pos = new Vector3(x * cell, 0f, (room.MinZ + index) * cell);
                rot = Quaternion.Euler(0f, -90f, 0f);
            }
        }

        static bool IsWallOwner(Vector2Int a, Vector2Int b)
        {
            if (a.x != b.x)
            {
                return a.x < b.x;
            }

            return a.y < b.y;
        }

        static void EnsureWalkableCollider(GameObject floorInstance, float cell)
        {
            if (floorInstance == null || floorInstance.GetComponent<BoxCollider>() != null)
            {
                return;
            }

            var box = floorInstance.AddComponent<BoxCollider>();
            box.center = new Vector3(0.5f * cell, -0.05f, 0.5f * cell);
            box.size = new Vector3(cell, 0.1f, cell);
        }

        GameObject Spawn(GameObject prefab, Transform parent, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, parent);
            instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
            return instance;
        }

        sealed class RoomData
        {
            public int Id;
            public Vector2Int GridPos;
            public int MinX;
            public int MinZ;
            public int Size;
            public List<Vector2Int> Connections = new List<Vector2Int>();

            public RoomData(Vector2Int gridPos) => GridPos = gridPos;
        }
    }
}
