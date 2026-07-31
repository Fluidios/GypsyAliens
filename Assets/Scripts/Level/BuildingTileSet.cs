using UnityEngine;

namespace GypsyAliens.Level
{
    [CreateAssetMenu(fileName = "BuildingTileSet", menuName = "GypsyAliens/Building Tile Set")]
    public sealed class BuildingTileSet : ScriptableObject
    {
        [SerializeField] GameObject _floorPrefab;
        [SerializeField] GameObject _wallPrefab;
        [SerializeField] GameObject _doorWallPrefab;
        [SerializeField] GameObject[] _roomPropPrefabs;
        [SerializeField] float _cellSize = 1f;

        public GameObject FloorPrefab => _floorPrefab;
        public GameObject WallPrefab => _wallPrefab;
        public GameObject DoorWallPrefab => _doorWallPrefab;
        public GameObject[] RoomPropPrefabs => _roomPropPrefabs;
        public float CellSize => _cellSize;
    }
}
