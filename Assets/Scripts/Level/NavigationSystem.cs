using System.Collections.Generic;
using GypsyAliens.Core;
using UnityEngine;

namespace GypsyAliens.Level
{
    /// <summary>
    /// Room-graph pathfinding through doorways. Reusable by players and future NPCs.
    /// </summary>
    public sealed class NavigationSystem : GameSystemBehaviour<NavigationSystem>
    {
        LevelNavigationMap _map;

        public LevelNavigationMap Map => _map;
        public bool HasMap => _map != null && _map.Rooms.Count > 0;

        public void SetMap(LevelNavigationMap map)
        {
            _map = map;
        }

        public void ClearMap()
        {
            _map = null;
        }

        /// <summary>
        /// Builds a polyline: optional door waypoints then the final destination.
        /// Returns false if start/end are outside the map.
        /// </summary>
        public bool TryFindPath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            if (!HasMap)
            {
                path.Add(Flat(to, from.y));
                return true;
            }

            from = Flat(from, from.y);
            to = Flat(to, from.y);

            if (!_map.TryFindRoomAt(from, out var startRoom) || !_map.TryFindRoomAt(to, out var endRoom))
            {
                // Fallback: direct move if either point is slightly outside due to wall thickness.
                if (TryFindNearestRoom(from, out startRoom) && TryFindNearestRoom(to, out endRoom))
                {
                    // continue
                }
                else
                {
                    path.Add(to);
                    return true;
                }
            }

            if (startRoom.Id == endRoom.Id)
            {
                path.Add(to);
                return true;
            }

            if (!TryBreadthFirst(startRoom.Id, endRoom.Id, out var roomChain))
            {
                path.Add(to);
                return false;
            }

            for (var i = 0; i < roomChain.Count - 1; i++)
            {
                var a = roomChain[i];
                var b = roomChain[i + 1];
                if (!TryGetDoorBetween(a, b, out var doorPos))
                {
                    continue;
                }

                path.Add(Flat(doorPos, from.y));
            }

            path.Add(to);
            return true;
        }

        bool TryFindNearestRoom(Vector3 point, out RoomNavNode room)
        {
            room = null;
            var best = float.MaxValue;
            foreach (var candidate in _map.Rooms)
            {
                var dx = Mathf.Max(candidate.Bounds.xMin - point.x, 0f, point.x - candidate.Bounds.xMax);
                var dz = Mathf.Max(candidate.Bounds.yMin - point.z, 0f, point.z - candidate.Bounds.yMax);
                var d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    room = candidate;
                }
            }

            return room != null;
        }

        bool TryBreadthFirst(int startId, int endId, out List<int> chain)
        {
            chain = null;
            var cameFrom = new Dictionary<int, int>();
            var queue = new Queue<int>();
            queue.Enqueue(startId);
            cameFrom[startId] = startId;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == endId)
                {
                    chain = Reconstruct(cameFrom, startId, endId);
                    return true;
                }

                if (!_map.TryGetRoom(current, out var node))
                {
                    continue;
                }

                foreach (var door in node.Doors)
                {
                    if (cameFrom.ContainsKey(door.ToRoomId))
                    {
                        continue;
                    }

                    cameFrom[door.ToRoomId] = current;
                    queue.Enqueue(door.ToRoomId);
                }
            }

            return false;
        }

        static List<int> Reconstruct(Dictionary<int, int> cameFrom, int startId, int endId)
        {
            var chain = new List<int>();
            var current = endId;
            chain.Add(current);
            while (current != startId)
            {
                current = cameFrom[current];
                chain.Add(current);
            }

            chain.Reverse();
            return chain;
        }

        bool TryGetDoorBetween(int fromId, int toId, out Vector3 doorPos)
        {
            doorPos = default;
            if (!_map.TryGetRoom(fromId, out var from))
            {
                return false;
            }

            foreach (var door in from.Doors)
            {
                if (door.ToRoomId == toId)
                {
                    doorPos = door.DoorPosition;
                    return true;
                }
            }

            return false;
        }

        static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    }
}
