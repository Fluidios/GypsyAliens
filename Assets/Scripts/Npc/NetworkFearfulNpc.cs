using System.Collections.Generic;
using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Host-authoritative fearful animal: idle, detect players in vision cone, flee to an empty room.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkFearfulNpc : NetworkBehaviour
    {
        enum AiState
        {
            Idle = 0,
            Flee = 1,
        }

        [SerializeField] float _moveSpeed = 2.4f;
        [SerializeField] float _turnSpeed = 540f;
        [SerializeField] float _arriveDistance = 0.35f;
        [SerializeField] float _visionRange = 8f;
        [SerializeField] float _visionNearRadius = 2.5f;
        [SerializeField] float _visionAngle = 70f;
        [SerializeField] float _eyeHeight = 0.4f;
        [SerializeField] float _detectInterval = 0.15f;
        [SerializeField] VisionConeView _visionCone;
        [SerializeField] CharacterController _characterController;

        readonly List<Vector3> _waypoints = new List<Vector3>(8);
        readonly List<RoomNavNode> _candidateRooms = new List<RoomNavNode>(16);

        AiState _state;
        int _waypointIndex;
        bool _hasPath;
        float _detectTimer;
        int _fleeTargetRoomId = -1;

        public override void Spawned()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_visionCone == null)
            {
                _visionCone = GetComponentInChildren<VisionConeView>(true);
            }

            if (_visionCone != null)
            {
                _visionCone.Configure(_visionRange, _visionNearRadius, _visionAngle);
            }

            _state = AiState.Idle;
            ClearPath();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            _detectTimer -= Runner.DeltaTime;
            if (_detectTimer <= 0f)
            {
                _detectTimer = _detectInterval;
                if (_state == AiState.Idle && TryDetectPlayer(out _))
                {
                    BeginFlee();
                }
            }

            if (_state == AiState.Flee)
            {
                TickFleeMovement();
            }
        }

        bool TryDetectPlayer(out Transform player)
        {
            player = null;
            if (_visionCone == null)
            {
                return false;
            }

            var players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || !p.Object || !p.Object.IsValid)
                {
                    continue;
                }

                // Only fear released / active players near the floor.
                var target = p.transform.position + Vector3.up * 0.9f;
                if (!_visionCone.ContainsPoint(target, transform.position.y + _eyeHeight))
                {
                    continue;
                }

                player = p.transform;
                return true;
            }

            return false;
        }

        void BeginFlee()
        {
            if (!TryPickFleeRoom(out var room))
            {
                return;
            }

            _fleeTargetRoomId = room.Id;
            var destination = room.Center;
            destination.y = transform.position.y;

            ClearPath();
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                && nav.TryFindPath(transform.position, destination, _waypoints)
                && _waypoints.Count > 0)
            {
                _hasPath = true;
                _waypointIndex = 0;
                _state = AiState.Flee;
            }
        }

        bool TryPickFleeRoom(out RoomNavNode room)
        {
            room = null;
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return false;
            }

            var map = nav.Map;
            if (!map.TryFindRoomAt(transform.position, out var current)
                && !TryNearestRoom(map, transform.position, out current))
            {
                return false;
            }

            CollectOccupiedRoomIds(map, out var occupied);

            _candidateRooms.Clear();
            // Prefer rooms reachable through a single door (neighbors), then any empty room.
            foreach (var door in current.Doors)
            {
                if (!map.TryGetRoom(door.ToRoomId, out var neighbor))
                {
                    continue;
                }

                if (occupied.Contains(neighbor.Id) || neighbor.Id == current.Id)
                {
                    continue;
                }

                _candidateRooms.Add(neighbor);
            }

            if (_candidateRooms.Count == 0)
            {
                foreach (var candidate in map.Rooms)
                {
                    if (candidate.Id == current.Id || occupied.Contains(candidate.Id))
                    {
                        continue;
                    }

                    _candidateRooms.Add(candidate);
                }
            }

            if (_candidateRooms.Count == 0)
            {
                return false;
            }

            room = _candidateRooms[Random.Range(0, _candidateRooms.Count)];
            return true;
        }

        static void CollectOccupiedRoomIds(LevelNavigationMap map, out HashSet<int> occupied)
        {
            occupied = new HashSet<int>();
            var players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null)
                {
                    continue;
                }

                if (map.TryFindRoomAt(p.transform.position, out var room))
                {
                    occupied.Add(room.Id);
                }
            }
        }

        static bool TryNearestRoom(LevelNavigationMap map, Vector3 point, out RoomNavNode room)
        {
            room = null;
            var best = float.MaxValue;
            foreach (var candidate in map.Rooms)
            {
                var c = candidate.Center;
                var d = (c.x - point.x) * (c.x - point.x) + (c.z - point.z) * (c.z - point.z);
                if (d < best)
                {
                    best = d;
                    room = candidate;
                }
            }

            return room != null;
        }

        void TickFleeMovement()
        {
            if (!_hasPath || _waypointIndex >= _waypoints.Count)
            {
                _state = AiState.Idle;
                ClearPath();
                return;
            }

            var target = _waypoints[_waypointIndex];
            target.y = transform.position.y;
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;

            if (distance <= _arriveDistance)
            {
                _waypointIndex++;
                if (_waypointIndex >= _waypoints.Count)
                {
                    _state = AiState.Idle;
                    ClearPath();
                }

                return;
            }

            var dir = toTarget / distance;
            var step = dir * (_moveSpeed * Runner.DeltaTime);
            if (_characterController != null && _characterController.enabled)
            {
                _characterController.Move(step);
            }
            else
            {
                transform.position += step;
            }

            var look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                look,
                _turnSpeed * Runner.DeltaTime);
        }

        void ClearPath()
        {
            _waypoints.Clear();
            _waypointIndex = 0;
            _hasPath = false;
            _fleeTargetRoomId = -1;
        }
    }
}
