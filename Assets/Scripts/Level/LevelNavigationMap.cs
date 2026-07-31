using System.Collections.Generic;
using UnityEngine;

namespace GypsyAliens.Level
{
    public sealed class RoomNavNode
    {
        public int Id;
        public Vector2Int GridPos;
        public Rect Bounds; // xz plane: xMin,yMin = world XZ min, width/height in world units
        public Vector3 Center;
        public readonly List<RoomDoorLink> Doors = new List<RoomDoorLink>();

        public bool Contains(Vector3 worldPoint)
        {
            return worldPoint.x >= Bounds.xMin && worldPoint.x <= Bounds.xMax
                   && worldPoint.z >= Bounds.yMin && worldPoint.z <= Bounds.yMax;
        }
    }

    public sealed class RoomDoorLink
    {
        public int FromRoomId;
        public int ToRoomId;
        public Vector3 DoorPosition;
    }

    /// <summary>
    /// Immutable snapshot of the generated room graph for pathfinding.
    /// </summary>
    public sealed class LevelNavigationMap
    {
        public readonly IReadOnlyList<RoomNavNode> Rooms;
        readonly Dictionary<int, RoomNavNode> _byId = new Dictionary<int, RoomNavNode>();

        public LevelNavigationMap(List<RoomNavNode> rooms)
        {
            Rooms = rooms;
            foreach (var room in rooms)
            {
                _byId[room.Id] = room;
            }
        }

        public bool TryGetRoom(int id, out RoomNavNode room) => _byId.TryGetValue(id, out room);

        public bool TryFindRoomAt(Vector3 worldPoint, out RoomNavNode room)
        {
            for (var i = 0; i < Rooms.Count; i++)
            {
                if (Rooms[i].Contains(worldPoint))
                {
                    room = Rooms[i];
                    return true;
                }
            }

            room = null;
            return false;
        }
    }
}
