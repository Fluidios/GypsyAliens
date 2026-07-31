using UnityEngine;

namespace GypsyAliens.Core
{
    /// <summary>
    /// Named physics layers used by click, collision, and x-ray systems.
    /// </summary>
    public static class GameLayers
    {
        public const string FloorName = "Floor";
        public const string WallName = "Wall";

        public static int Floor => LayerMask.NameToLayer(FloorName);
        public static int Wall => LayerMask.NameToLayer(WallName);

        public static LayerMask FloorMask => Floor >= 0 ? (1 << Floor) : ~0;
        public static LayerMask WallMask => Wall >= 0 ? (1 << Wall) : 0;

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null || layer < 0)
            {
                return;
            }

            go.layer = layer;
            var t = go.transform;
            for (var i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
            }
        }
    }
}
