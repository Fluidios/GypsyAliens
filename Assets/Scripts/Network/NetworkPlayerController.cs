using System.Collections.Generic;
using Fusion;
using GypsyAliens.Cameras;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Npc;
using UnityEngine;

namespace GypsyAliens.Network
{
    [RequireComponent(typeof(NetworkCharacterController))]
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        [SerializeField] float _holdHeight = 2f;
        [SerializeField] float _arriveDistance = 0.4f;
        [SerializeField] float _dragRange = 2.2f;
        [SerializeField] NetworkObject _rockPrefab;
        [SerializeField] float _rockSpawnHeight = 0.6f;
        [SerializeField] float _rockStunDuration = 3.5f;
        [SerializeField] StunStarsEffect _stunStars;

        NetworkCharacterController _ncc;
        CharacterController _characterController;
        bool _cameraBound;
        bool _released;
        Vector3 _holdPosition;
        float _stunLeft;

        readonly List<Vector3> _waypoints = new List<Vector3>(8);
        int _waypointIndex;
        bool _hasPath;

        NetworkFearfulNpc _draggedNpc;
        float _baseMaxSpeed = 2f;
        float _respawnInvulnLeft;

        [Networked] public NetworkBool IsStunned { get; set; }

        /// <summary>True while this player is pathfinding / walking this tick.</summary>
        public bool IsActivelyMoving => _hasPath && _waypointIndex < _waypoints.Count;

        public override void Spawned()
        {
            _ncc = GetComponent<NetworkCharacterController>();
            _characterController = GetComponent<CharacterController>();
            if (_ncc != null)
            {
                _baseMaxSpeed = _ncc.maxSpeed;
            }

            if (_stunStars == null)
            {
                _stunStars = GetComponentInChildren<StunStarsEffect>(true);
                if (_stunStars == null)
                {
                    var starsGo = new GameObject("StunStars");
                    starsGo.transform.SetParent(transform, false);
                    _stunStars = starsGo.AddComponent<StunStarsEffect>();
                }
            }

            _holdPosition = transform.position + Vector3.up * _holdHeight;
            _released = false;
            _respawnInvulnLeft = 0f;
            _stunLeft = 0f;
            IsStunned = false;
            ClearPath();
            RefreshStunVisual(false);

            if (GetComponent<PlayerThrowAimView>() == null && HasInputAuthority)
            {
                gameObject.AddComponent<PlayerThrowAimView>();
            }

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

        public override void Render()
        {
            if (!_released)
            {
                transform.position = _holdPosition;
            }

            RefreshStunVisual(IsStunned);
            TryBindCamera();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                level.LevelReady -= OnLevelReady;
            }

            ReleaseDrag();
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

            if (_respawnInvulnLeft > 0f)
            {
                _respawnInvulnLeft -= Runner.DeltaTime;
            }

            if (IsStunned || _stunLeft > 0f)
            {
                TickStunned();
                return;
            }

            if (!GetInput(out NetworkPlayerInput input))
            {
                return;
            }

            if (NetworkGameSession.Instance != null && NetworkGameSession.Instance.LevelCompleted)
            {
                ReleaseDrag();
                ClearPath();
                _ncc.Velocity = Vector3.zero;
                _ncc.Move(Vector3.zero);
                return;
            }

            if (input.ThrowReleased && Runner.IsServer)
            {
                TryThrowRock(input);
            }

            if (input.SpaceHeld)
            {
                TickDrag(true);
            }
            else
            {
                ReleaseDrag();
            }

            // Player can keep walking while holding Space and dragging.
            if (input.SetMoveTarget)
            {
                var destination = new Vector3(input.MoveTarget.x, transform.position.y, input.MoveTarget.y);
                BuildPathTo(destination);
            }

            if (!_hasPath || _waypointIndex >= _waypoints.Count)
            {
                ApplyDragMoveSpeed();
                _ncc.Velocity = Vector3.zero;
                _ncc.Move(Vector3.zero);
                ClampUnexpectedLaunch();
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
                    ApplyDragMoveSpeed();
                    _ncc.Velocity = Vector3.zero;
                    _ncc.Move(Vector3.zero);
                }

                ClampUnexpectedLaunch();
                return;
            }

            ApplyDragMoveSpeed();
            _ncc.Move(toTarget / distance);
            ClampUnexpectedLaunch();
        }

        void TickStunned()
        {
            ReleaseDrag();
            ClearPath();
            if (_ncc != null)
            {
                _ncc.Velocity = Vector3.zero;
                _ncc.Move(Vector3.zero);
            }

            if (!HasStateAuthority)
            {
                return;
            }

            _stunLeft -= Runner.DeltaTime;
            if (_stunLeft > 0f)
            {
                IsStunned = true;
                return;
            }

            _stunLeft = 0f;
            IsStunned = false;
            RefreshStunVisual(false);
        }

        /// <summary>
        /// Rock hit stun — host-authoritative. Blocks move / throw / drag while active.
        /// </summary>
        public void ApplyStun(float duration = -1f)
        {
            if (!HasStateAuthority || !_released)
            {
                return;
            }

            if (_respawnInvulnLeft > 0f)
            {
                return;
            }

            _stunLeft = duration > 0f ? duration : _rockStunDuration;
            IsStunned = true;
            ReleaseDrag();
            ClearPath();
            RefreshStunVisual(true);
        }

        void ApplyDragMoveSpeed()
        {
            if (_ncc == null)
            {
                return;
            }

            if (_draggedNpc != null && _draggedNpc.Object && _draggedNpc.Object.IsValid && _draggedNpc.IsDragged)
            {
                _ncc.maxSpeed = _baseMaxSpeed * _draggedNpc.GetDragSpeedFactor();
            }
            else
            {
                _ncc.maxSpeed = _baseMaxSpeed;
            }
        }

        void TryThrowRock(NetworkPlayerInput input)
        {
            if (IsStunned)
            {
                return;
            }

            if (_rockPrefab == null)
            {
                if (SystemLocator.Instance != null
                    && SystemLocator.Instance.TryGet<NetworkService>(out var net))
                {
                    _rockPrefab = net.RockPrefab;
                }
            }

            if (_rockPrefab == null || Runner == null)
            {
                return;
            }

            var end = new Vector3(input.AimPoint.x, 0.12f, input.AimPoint.y);
            var flat = end - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
            {
                flat = transform.forward;
            }

            flat.Normalize();
            var start = transform.position + Vector3.up * _rockSpawnHeight + flat * 0.35f;
            var distance = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z));
            var arcHeight = ThrownRock.ComputeArcHeight(distance);
            if (ThrownRock.IsTrajectoryBlocked(
                    start, end, arcHeight, ThrownRock.DefaultWallRadius, GameLayers.WallMask))
            {
                return;
            }

            var rockObj = Runner.Spawn(_rockPrefab, start, Quaternion.identity, Object.InputAuthority);
            if (rockObj != null && rockObj.TryGetComponent<ThrownRock>(out var rock))
            {
                rock.Init(start, end);
            }
        }

        void TickDrag(bool holding)
        {
            if (!HasStateAuthority || IsStunned)
            {
                return;
            }

            if (!holding)
            {
                ReleaseDrag();
                return;
            }

            if (_draggedNpc != null && _draggedNpc.Object && _draggedNpc.Object.IsValid)
            {
                return;
            }

            if (!TryFindNearestDraggable(out var npc))
            {
                return;
            }

            _draggedNpc = npc;
            npc.BeginDrag(this);
            ClearPath();
        }

        void ReleaseDrag()
        {
            if (_draggedNpc != null)
            {
                if (_draggedNpc.Object && _draggedNpc.Object.IsValid && !_draggedNpc.IsExtracting)
                {
                    _draggedNpc.EndDrag(this);
                }

                _draggedNpc = null;
            }

            ApplyDragMoveSpeed();
        }

        public void ClearDraggedNpc(NetworkFearfulNpc npc)
        {
            if (_draggedNpc == npc)
            {
                _draggedNpc = null;
            }
        }

        /// <summary>
        /// Hostile rifle kill — respawn at the level start point.
        /// </summary>
        public void ApplyRifleHit(float duration = -1f)
        {
            if (!HasStateAuthority || !_released)
            {
                return;
            }

            if (_respawnInvulnLeft > 0f)
            {
                return;
            }

            RespawnAtStart();
        }

        void RespawnAtStart()
        {
            ReleaseDrag();
            ClearPath();
            _stunLeft = 0f;
            IsStunned = false;
            RefreshStunVisual(false);
            _respawnInvulnLeft = 1.25f;

            var spawn = transform.position;
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                && level.HasSpawnPoint)
            {
                spawn = level.SpawnPosition;
            }

            if (_ncc != null && Runner != null)
            {
                _ncc.Teleport(spawn);
                _ncc.Velocity = Vector3.zero;
            }
            else
            {
                transform.position = spawn;
            }
        }

        /// <summary>
        /// CharacterController depenetration against prop MeshColliders can yeet the player upward.
        /// Undo extreme vertical spikes (there is no jump in this prototype).
        /// </summary>
        void ClampUnexpectedLaunch()
        {
            if (_ncc == null)
            {
                return;
            }

            var v = _ncc.Velocity;
            if (v.y > 0.05f)
            {
                v.y = 0f;
                _ncc.Velocity = v;
            }

            var pos = transform.position;
            if (pos.y <= 2.5f)
            {
                return;
            }

            var groundedY = 0.15f;
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                && level.HasSpawnPoint)
            {
                groundedY = level.SpawnPosition.y;
            }

            pos.y = groundedY;
            _ncc.Teleport(pos);
            _ncc.Velocity = Vector3.zero;
        }

        bool TryFindNearestDraggable(out NetworkFearfulNpc npc)
        {
            npc = null;
            var best = _dragRange * _dragRange;
            var animals = FindObjectsByType<NetworkFearfulNpc>(FindObjectsSortMode.None);
            for (var i = 0; i < animals.Length; i++)
            {
                var a = animals[i];
                if (a == null || !a.CanAcceptDrag(this))
                {
                    continue;
                }

                var d = a.transform.position - transform.position;
                d.y = 0f;
                var sqr = d.sqrMagnitude;
                if (sqr <= best)
                {
                    best = sqr;
                    npc = a;
                }
            }

            return npc != null;
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

        void RefreshStunVisual(bool stunned)
        {
            if (_stunStars != null)
            {
                _stunStars.SetActive(stunned);
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
