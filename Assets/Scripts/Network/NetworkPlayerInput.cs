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

        /// <summary>Space is held this tick (drag when near an animal).</summary>
        public NetworkBool SpaceHeld;

        /// <summary>True the tick Space was released / pressed to throw a rock.</summary>
        public NetworkBool ThrowReleased;

        /// <summary>Floor aim point XZ for throw landing.</summary>
        public Vector2 AimPoint;
    }
}
