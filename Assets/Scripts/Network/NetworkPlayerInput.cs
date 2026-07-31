using Fusion;
using UnityEngine;

namespace GypsyAliens.Network
{
    public struct NetworkPlayerInput : INetworkInput
    {
        /// <summary>World XZ destination for click-to-move.</summary>
        public Vector2 MoveTarget;

        /// <summary>True on the tick the player issued a new click destination.</summary>
        public NetworkBool SetMoveTarget;
    }
}
