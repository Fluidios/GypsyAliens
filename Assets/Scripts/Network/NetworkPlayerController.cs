using System.Collections.Generic;
using Fusion;
using GypsyAliens.Cameras;
using GypsyAliens.Core;
using GypsyAliens.Level;
using UnityEngine;

namespace GypsyAliens.Network
{
    [RequireComponent(typeof(NetworkCharacterController))]
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        [SerializeField] float _holdHeight = 2f;
        [SerializeField] float _arriveDistance = 0.4f;

        NetworkCharacterController _ncc;
        CharacterController _characterController;
        bool _cameraBound;
        bool _released;
        Vector3 _holdPosition;

        readonly List<Vector3> _waypoints = new List<Vector3>(8);
        int _waypointIndex;
        bool _hasPath;

        public override void Spawned()
        {
            _ncc = GetComponent<NetworkCharacterController>();
            _characterController = GetComponent<CharacterController>();
            _holdPosition = transform.position + Vector3.up * _holdHeight;
            _released = false;
            ClearPath();

            FreezeMovement(true);
            HoldAt(_holdPosition);

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                if (level.IsReady)
                {
                    ReleaseToSpawn(level);
                }
                else
                {
                    level.LevelReady += OnLevelReady;
                }
            }

            TryBindCamera();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                level.LevelReady -= OnLevelReady;
            }
        }

        void OnLevelReady()
        {
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                return;
            }

            level.LevelReady -= OnLevelReady;
            ReleaseToSpawn(level);
        }

        void ReleaseToSpawn(LevelGenerationSystem level)
        {
            if (_released)
            {
                return;
            }

            var spawn = level.HasSpawnPoint
                ? level.SpawnPosition
                : new Vector3(0f, 0.15f, 0f);

            _released = true;
            _holdPosition = spawn;
            ClearPath();
            FreezeMovement(false);

            if (_ncc != null && Runner != null)
            {
                _ncc.Teleport(spawn);
            }
            else
            {
                transform.position = spawn;
            }

            TryBindCamera();
        }

        public override void FixedUpdateNetwork()
        {
            if (!_released)
            {
                if (_ncc != null)
                {
                    _ncc.Velocity = Vector3.zero;
                }

                HoldAt(_holdPosition);
                return;
            }

            if (GetInput(out NetworkPlayerInput input) && input.SetMoveTarget)
            {
                var destination = new Vector3(input.MoveTarget.x, transform.position.y, input.MoveTarget.y);
                BuildPathTo(destination);
            }

            if (!_hasPath || _waypointIndex >= _waypoints.Count)
            {
                _ncc.Velocity = Vector3.zero;
                _ncc.Move(Vector3.zero);
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
                    ClearPath();
                    _ncc.Velocity = Vector3.zero;
                    _ncc.Move(Vector3.zero);
                }

                return;
            }

            _ncc.Move(toTarget / distance);
        }

        void BuildPathTo(Vector3 destination)
        {
            ClearPath();

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                && nav.TryFindPath(transform.position, destination, _waypoints))
            {
                _hasPath = _waypoints.Count > 0;
                _waypointIndex = 0;
                return;
            }

            _waypoints.Add(destination);
            _hasPath = true;
            _waypointIndex = 0;
        }

        void ClearPath()
        {
            _waypoints.Clear();
            _waypointIndex = 0;
            _hasPath = false;
        }

        public override void Render()
        {
            if (!_released)
            {
                transform.position = _holdPosition;
            }

            TryBindCamera();
        }

        void FreezeMovement(bool freeze)
        {
            if (_ncc != null)
            {
                _ncc.Velocity = Vector3.zero;
            }

            if (_characterController != null)
            {
                _characterController.enabled = !freeze;
            }
        }

        void HoldAt(Vector3 position)
        {
            _holdPosition = position;

            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.position = position;

            if (_ncc != null)
            {
                _ncc.Velocity = Vector3.zero;
            }
        }

        void TryBindCamera()
        {
            if (_cameraBound || !HasInputAuthority)
            {
                return;
            }

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<CameraSystem>(out var cameraSystem))
            {
                cameraSystem.SetFollowTarget(transform);
                _cameraBound = true;
            }
        }
    }
}
