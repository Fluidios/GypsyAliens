using System.Collections.Generic;
using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Hostile homeowner: alert threshold then aims/shoots when the player is seen;
    /// investigates last known position; wanders across rooms. Host-authoritative.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkHostileNpc : NetworkBehaviour
    {
        enum AiState
        {
            Wander = 0,
            Investigate = 1,
            Search = 2,
            Alert = 3,
            Aiming = 4,
            Shooting = 5,
            Stunned = 6,
        }

        [SerializeField] float _walkSpeed = 1.1f;
        [SerializeField] float _runSpeed = 3.2f;
        [SerializeField] float _turnSpeed = 360f;
        [SerializeField] float _arriveDistance = 0.45f;
        [SerializeField] float _visionRange = 10f;
        [SerializeField] float _visionNearRadius = 2.2f;
        [SerializeField] float _visionAngle = 65f;
        [SerializeField] float _eyeHeight = 1.5f;
        [SerializeField] float _detectInterval = 0.12f;
        [SerializeField] float _alertThreshold = 1.5f;
        [SerializeField] float _aimDuration = 3f;
        [SerializeField] float _shootDuration = 0.45f;
        [SerializeField] float _searchDuration = 4.5f;
        [SerializeField] float _searchSweepHalfAngle = 60f;
        [SerializeField] float _searchSweepSpeed = 55f;
        [SerializeField] float _roomChangeInterval = 20f;
        [SerializeField] float _wanderPauseMin = 0.6f;
        [SerializeField] float _wanderPauseMax = 1.8f;
        [SerializeField] float _roomMargin = 0.8f;
        [SerializeField] float _stunDuration = 8f;
        [SerializeField] float _shotDamageSeconds = 2.5f;
        [SerializeField] VisionConeView _visionCone;
        [SerializeField] CharacterController _characterController;
        [SerializeField] StunStarsEffect _stunStars;
        [SerializeField] HostileAnimationDriver _animDriver;
        [SerializeField] float _noiseCheckInterval = 0.2f;
        [SerializeField] HostileStatusIconView _statusIcon;
        [SerializeField] HostileAimFxView _aimFx;
        [SerializeField] Transform _muzzle;
        [SerializeField] LayerMask _shotMask = ~0;

        readonly List<Vector3> _waypoints = new List<Vector3>(12);
        readonly List<RoomNavNode> _candidateRooms = new List<RoomNavNode>(16);

        AiState _state;
        int _waypointIndex;
        bool _hasPath;
        float _detectTimer;
        float _noiseTimer;
        float _wanderPauseLeft;
        float _roomChangeLeft;
        float _alertLeft;
        float _aimLeft;
        float _shootLeft;
        float _searchLeft;
        float _stunLeft;
        float _searchYawDir = 1f;
        float _searchBaseYaw;
        Vector3 _lastKnownPlayerPos;
        bool _hasLastKnown;
        Vector3 _distractPoint;
        bool _hasDistractPoint;
        NetworkPlayerController _aimTarget;
        int _spawnRoomId = -1;
        bool _spawnRoomCached;

        [Networked] public NetworkBool IsStunned { get; set; }
        [Networked] public NetworkBool IsAiming { get; set; }
        [Networked] public NetworkBool IsShooting { get; set; }
        [Networked] public byte StatusIcon { get; set; }
        [Networked] public float AlertFill { get; set; }
        [Networked] public float AimProgress { get; set; }
        [Networked] public NetworkId AimTargetId { get; set; }

        public override void Spawned()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            if (_visionCone == null)
            {
                _visionCone = GetComponentInChildren<VisionConeView>(true);
            }

            if (_stunStars == null)
            {
                _stunStars = GetComponentInChildren<StunStarsEffect>(true);
                if (_stunStars == null)
                {
                    var starsGo = new GameObject("StunStars");
                    starsGo.transform.SetParent(transform, false);
                    starsGo.transform.localPosition = Vector3.up * 2f;
                    _stunStars = starsGo.AddComponent<StunStarsEffect>();
                }
            }

            if (_animDriver == null)
            {
                _animDriver = GetComponent<HostileAnimationDriver>();
            }

            if (_statusIcon == null)
            {
                _statusIcon = GetComponentInChildren<HostileStatusIconView>(true);
                if (_statusIcon == null)
                {
                    _statusIcon = gameObject.AddComponent<HostileStatusIconView>();
                }
            }

            if (_aimFx == null)
            {
                _aimFx = GetComponentInChildren<HostileAimFxView>(true);
                if (_aimFx == null)
                {
                    _aimFx = gameObject.AddComponent<HostileAimFxView>();
                }
            }

            if (_aimFx != null && _muzzle != null)
            {
                _aimFx.SetLineOrigin(_muzzle);
            }

            if (_visionCone != null)
            {
                _visionCone.Configure(_visionRange, _visionNearRadius, _visionAngle);
                _visionCone.SetAlertFill(0f);
            }

            EnsureHitCollider();
            _state = AiState.Wander;
            _roomChangeLeft = _roomChangeInterval;
            _wanderPauseLeft = Random.Range(0.2f, 0.8f);
            ClearPath();
            SetVisionActive(true);
            RefreshStunVisual(false);
            IsAiming = false;
            IsShooting = false;
            AimTargetId = default;
            AimProgress = 0f;
            StatusIcon = (byte)HostileStatusIconView.IconKind.None;
            AlertFill = 0f;
        }

        public override void Render()
        {
            RefreshStunVisual(IsStunned);
            SetVisionActive(!IsStunned);
            if (_visionCone != null)
            {
                _visionCone.SetAlertFill(AlertFill);
            }

            if (_statusIcon != null)
            {
                _statusIcon.SetIcon((HostileStatusIconView.IconKind)StatusIcon);
            }

            RefreshAimFx();

            if (_animDriver != null)
            {
                _animDriver.SetCombat(IsAiming, IsShooting);
                _animDriver.SetSearching(
                    !IsAiming
                    && !IsShooting
                    && StatusIcon == (byte)HostileStatusIconView.IconKind.Question
                    && AlertFill <= 0.001f);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            if (_state == AiState.Stunned || IsStunned)
            {
                _state = AiState.Stunned;
                TickStunned();
                return;
            }

            _detectTimer -= Runner.DeltaTime;
            var ranDetect = false;
            var seesPlayer = false;
            NetworkPlayerController spotted = null;
            if (_detectTimer <= 0f || _state == AiState.Alert)
            {
                if (_detectTimer <= 0f)
                {
                    _detectTimer = _detectInterval;
                }

                ranDetect = true;
                seesPlayer = TryDetectPlayer(out spotted);
            }

            if (seesPlayer && spotted != null)
            {
                _lastKnownPlayerPos = spotted.transform.position;
                _hasLastKnown = true;
                _aimTarget = spotted;

                if (_state == AiState.Wander
                    || _state == AiState.Investigate
                    || _state == AiState.Search)
                {
                    BeginAlert(spotted);
                }
            }
            else if (ranDetect && _state == AiState.Alert)
            {
                CancelAlert();
            }
            else if (ranDetect && _state == AiState.Aiming)
            {
                BeginInvestigate();
            }

            _noiseTimer -= Runner.DeltaTime;
            if (_noiseTimer <= 0f)
            {
                _noiseTimer = _noiseCheckInterval;
                TryHearNoise();
            }

            switch (_state)
            {
                case AiState.Alert:
                    TickAlert();
                    break;
                case AiState.Aiming:
                    TickAiming();
                    break;
                case AiState.Shooting:
                    TickShooting();
                    break;
                case AiState.Investigate:
                    TickInvestigate();
                    break;
                case AiState.Search:
                    TickSearch();
                    break;
                default:
                    TickWander();
                    break;
            }
        }

        public void ApplyStun(float duration = -1f)
        {
            ApplyStun(null, duration);
        }

        /// <summary>
        /// Stun from a thrown rock. After recovery the hostile investigates <paramref name="distractFrom"/>.
        /// </summary>
        public void ApplyStun(Vector3? distractFrom, float duration = -1f)
        {
            if (!HasStateAuthority)
            {
                return;
            }

            _stunLeft = duration > 0f ? duration : _stunDuration;
            _state = AiState.Stunned;
            IsStunned = true;
            IsAiming = false;
            IsShooting = false;
            AimProgress = 0f;
            _aimTarget = null;
            AimTargetId = default;
            ClearAlertVisuals();
            ClearPath();
            SetVisionActive(false);
            RefreshStunVisual(true);
            SetStatus(HostileStatusIconView.IconKind.None);

            if (distractFrom.HasValue)
            {
                _distractPoint = distractFrom.Value;
                _distractPoint.y = transform.position.y;
                _hasDistractPoint = true;
            }
        }

        void BeginAlert(NetworkPlayerController target)
        {
            _aimTarget = target;
            _state = AiState.Alert;
            _alertLeft = _alertThreshold;
            IsAiming = false;
            IsShooting = false;
            AimTargetId = default;
            ClearPath();
            AlertFill = 0f;
            SetStatus(HostileStatusIconView.IconKind.Question);
        }

        void TickAlert()
        {
            if (_aimTarget == null || !_aimTarget.Object || !_aimTarget.Object.IsValid)
            {
                CancelAlert();
                return;
            }

            FacePoint(_aimTarget.transform.position);
            _lastKnownPlayerPos = _aimTarget.transform.position;
            _hasLastKnown = true;

            var targetPoint = _aimTarget.transform.position + Vector3.up * 0.9f;
            if (_visionCone == null
                || !_visionCone.ContainsPoint(targetPoint, transform.position.y + _eyeHeight))
            {
                CancelAlert();
                return;
            }

            _alertLeft -= Runner.DeltaTime;
            var elapsed = _alertThreshold - _alertLeft;
            AlertFill = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _alertThreshold));
            SetStatus(HostileStatusIconView.IconKind.Question);

            if (_alertLeft > 0f)
            {
                return;
            }

            AlertFill = 0f;
            BeginAim(_aimTarget);
        }

        void CancelAlert()
        {
            // Player left the cone before the threshold finished — no pursuit.
            ClearAlertVisuals();
            _aimTarget = null;
            AimTargetId = default;
            AimProgress = 0f;
            IsAiming = false;
            IsShooting = false;
            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
            SetStatus(HostileStatusIconView.IconKind.None);
        }

        void ClearAlertVisuals()
        {
            AlertFill = 0f;
        }

        void BeginAim(NetworkPlayerController target)
        {
            _aimTarget = target;
            _state = AiState.Aiming;
            _aimLeft = _aimDuration;
            AimProgress = 0f;
            IsAiming = true;
            IsShooting = false;
            ClearPath();
            ClearAlertVisuals();
            SetStatus(HostileStatusIconView.IconKind.Exclamation);
            SetAimTarget(target);
        }

        void TickAiming()
        {
            if (_aimTarget == null || !_aimTarget.Object || !_aimTarget.Object.IsValid)
            {
                BeginInvestigate();
                return;
            }

            FacePoint(_aimTarget.transform.position);
            _lastKnownPlayerPos = _aimTarget.transform.position;
            _hasLastKnown = true;
            SetStatus(HostileStatusIconView.IconKind.Exclamation);

            var targetPoint = _aimTarget.transform.position + Vector3.up * 0.9f;
            if (_visionCone == null
                || !_visionCone.ContainsPoint(targetPoint, transform.position.y + _eyeHeight))
            {
                BeginInvestigate();
                return;
            }

            _aimLeft -= Runner.DeltaTime;
            AimProgress = 1f - Mathf.Clamp01(_aimLeft / Mathf.Max(0.01f, _aimDuration));
            if (_aimLeft > 0f)
            {
                return;
            }

            AimProgress = 1f;
            _state = AiState.Shooting;
            _shootLeft = _shootDuration;
            IsShooting = true;
            FireAtTarget(_aimTarget);
        }

        void TickShooting()
        {
            if (_aimTarget != null && _aimTarget.Object && _aimTarget.Object.IsValid)
            {
                FacePoint(_aimTarget.transform.position);
            }

            SetStatus(HostileStatusIconView.IconKind.Exclamation);
            _shootLeft -= Runner.DeltaTime;
            if (_shootLeft > 0f)
            {
                return;
            }

            IsShooting = false;

            if (_aimTarget != null
                && _aimTarget.Object
                && _aimTarget.Object.IsValid
                && _visionCone != null
                && _visionCone.ContainsPoint(
                    _aimTarget.transform.position + Vector3.up * 0.9f,
                    transform.position.y + _eyeHeight))
            {
                BeginAim(_aimTarget);
            }
            else
            {
                BeginInvestigate();
            }
        }

        void FireAtTarget(NetworkPlayerController target)
        {
            if (target == null)
            {
                return;
            }

            var origin = _muzzle != null
                ? _muzzle.position
                : transform.position + Vector3.up * _eyeHeight + transform.forward * 0.4f;
            var aimPoint = target.transform.position + Vector3.up * 0.9f;
            var to = aimPoint - origin;
            var dist = to.magnitude;
            if (dist < 0.05f)
            {
                target.ApplyRifleHit(_shotDamageSeconds);
                return;
            }

            var dir = to / dist;
            if (Physics.Raycast(origin, dir, out var hit, dist + 0.25f, _shotMask, QueryTriggerInteraction.Ignore))
            {
                var hitPlayer = hit.collider.GetComponentInParent<NetworkPlayerController>();
                if (hitPlayer != null)
                {
                    hitPlayer.ApplyRifleHit(_shotDamageSeconds);
                }

                return;
            }

            target.ApplyRifleHit(_shotDamageSeconds);
        }

        void BeginInvestigate()
        {
            IsAiming = false;
            IsShooting = false;
            AimProgress = 0f;
            _aimTarget = null;
            AimTargetId = default;
            ClearAlertVisuals();
            if (!_hasLastKnown)
            {
                _state = AiState.Wander;
                _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
                SetStatus(HostileStatusIconView.IconKind.None);
                return;
            }

            _state = AiState.Investigate;
            SetStatus(HostileStatusIconView.IconKind.Question);
            SetPathTo(_lastKnownPlayerPos);
            if (!_hasPath)
            {
                BeginSearch();
            }
        }

        void TickInvestigate()
        {
            SetStatus(HostileStatusIconView.IconKind.Question);
            if (!_hasPath)
            {
                BeginSearch();
                return;
            }

            TickPathMovement(_runSpeed);
            if (!_hasPath)
            {
                BeginSearch();
            }
        }

        void BeginSearch()
        {
            _state = AiState.Search;
            _searchLeft = _searchDuration;
            _searchYawDir = Random.value < 0.5f ? -1f : 1f;
            _searchBaseYaw = transform.eulerAngles.y;
            ClearPath();
            IsAiming = false;
            IsShooting = false;
            AimTargetId = default;
            AimProgress = 0f;
            ClearAlertVisuals();
            SetStatus(HostileStatusIconView.IconKind.Question);
        }

        void TickSearch()
        {
            SetStatus(HostileStatusIconView.IconKind.Question);

            var currentYaw = transform.eulerAngles.y;
            var deltaFromBase = Mathf.DeltaAngle(_searchBaseYaw, currentYaw);
            if (deltaFromBase >= _searchSweepHalfAngle)
            {
                _searchYawDir = -1f;
            }
            else if (deltaFromBase <= -_searchSweepHalfAngle)
            {
                _searchYawDir = 1f;
            }

            var targetYaw = _searchBaseYaw + _searchYawDir * _searchSweepHalfAngle;
            var targetRot = Quaternion.Euler(0f, targetYaw, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                _searchSweepSpeed * Runner.DeltaTime);

            if (TryDetectPlayer(out var spotted))
            {
                _lastKnownPlayerPos = spotted.transform.position;
                _hasLastKnown = true;
                BeginAlert(spotted);
                return;
            }

            _searchLeft -= Runner.DeltaTime;
            if (_searchLeft > 0f)
            {
                return;
            }

            _hasLastKnown = false;
            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
            _roomChangeLeft = Mathf.Min(_roomChangeLeft, 2f);
            SetStatus(HostileStatusIconView.IconKind.None);
        }

        void TickWander()
        {
            SetStatus(HostileStatusIconView.IconKind.None);
            EnsureSpawnRoomCached();

            // If somehow standing in the spawn room while only wandering, leave immediately.
            if (IsInSpawnRoom(transform.position) && !_hasPath)
            {
                if (TryPickOtherRoom(out var escapeRoom))
                {
                    SetPathTo(escapeRoom.Center);
                    return;
                }
            }

            _roomChangeLeft -= Runner.DeltaTime;
            if (_roomChangeLeft <= 0f)
            {
                _roomChangeLeft = _roomChangeInterval;
                if (TryPickOtherRoom(out var room))
                {
                    SetPathTo(room.Center);
                    return;
                }
            }

            if (_hasPath)
            {
                TickPathMovement(_walkSpeed);
                if (!_hasPath)
                {
                    _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
                }

                return;
            }

            _wanderPauseLeft -= Runner.DeltaTime;
            if (_wanderPauseLeft > 0f)
            {
                return;
            }

            if (TryPickWanderPoint(out var point))
            {
                SetPathTo(point);
            }
            else
            {
                _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
            }
        }

        void TickStunned()
        {
            IsAiming = false;
            IsShooting = false;
            AimTargetId = default;
            ClearAlertVisuals();
            SetStatus(HostileStatusIconView.IconKind.None);
            _stunLeft -= Runner.DeltaTime;
            if (_stunLeft > 0f)
            {
                return;
            }

            IsStunned = false;
            RefreshStunVisual(false);
            SetVisionActive(true);

            if (_hasDistractPoint)
            {
                _lastKnownPlayerPos = _distractPoint;
                _hasLastKnown = true;
                _hasDistractPoint = false;
                FacePoint(_distractPoint);
                BeginInvestigate();
                return;
            }

            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(0.4f, 1f);
        }

        void SetStatus(HostileStatusIconView.IconKind kind)
        {
            StatusIcon = (byte)kind;
        }

        void SetAimTarget(NetworkPlayerController target)
        {
            if (target != null && target.Object != null && target.Object.IsValid)
            {
                AimTargetId = target.Object.Id;
            }
            else
            {
                AimTargetId = default;
            }
        }

        void RefreshAimFx()
        {
            if (_aimFx == null)
            {
                return;
            }

            var show = (IsAiming || IsShooting) && AimTargetId.IsValid;
            Transform target = null;
            if (show && Runner != null && Runner.TryFindObject(AimTargetId, out var obj) && obj != null)
            {
                target = obj.transform;
            }

            var progress = IsShooting ? 1f : AimProgress;
            _aimFx.SetActive(show && target != null, target, progress);
        }

        void TryHearNoise()
        {
            if (IsStunned
                || _state == AiState.Aiming
                || _state == AiState.Shooting
                || _state == AiState.Alert
                || _state == AiState.Stunned)
            {
                return;
            }

            if (!NoiseRegistry.TryGetAudible(transform.position, out var noisePos, out _))
            {
                return;
            }

            // Already investigating this noise area — keep current path.
            if (_hasLastKnown
                && (_state == AiState.Investigate || _state == AiState.Search)
                && (noisePos - _lastKnownPlayerPos).sqrMagnitude < 2.25f)
            {
                return;
            }

            _lastKnownPlayerPos = noisePos;
            _lastKnownPlayerPos.y = transform.position.y;
            _hasLastKnown = true;
            BeginInvestigate();
        }

        bool TryDetectPlayer(out NetworkPlayerController player)
        {
            player = null;
            if (IsStunned || _visionCone == null || !_visionCone.isActiveAndEnabled)
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

                var target = p.transform.position + Vector3.up * 0.9f;
                if (!_visionCone.ContainsPoint(target, transform.position.y + _eyeHeight))
                {
                    continue;
                }

                player = p;
                return true;
            }

            return false;
        }

        bool TryPickWanderPoint(out Vector3 point)
        {
            point = transform.position;
            EnsureSpawnRoomCached();
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return false;
            }

            var map = nav.Map;
            if (!map.TryFindRoomAt(transform.position, out var room)
                && !TryNearestRoom(map, transform.position, out room))
            {
                return false;
            }

            // Never idle-wander inside the player spawn room — leave to another room.
            if (IsSpawnRoom(room))
            {
                if (TryPickOtherRoom(out var other))
                {
                    point = other.Center;
                    point.y = transform.position.y;
                    return true;
                }

                return false;
            }

            var b = room.Bounds;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var x = Random.Range(b.xMin + _roomMargin, b.xMax - _roomMargin);
                var z = Random.Range(b.yMin + _roomMargin, b.yMax - _roomMargin);
                var candidate = new Vector3(x, transform.position.y, z);
                if ((candidate - transform.position).sqrMagnitude > 0.4f)
                {
                    point = candidate;
                    return true;
                }
            }

            return false;
        }

        bool TryPickOtherRoom(out RoomNavNode room)
        {
            room = null;
            EnsureSpawnRoomCached();
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return false;
            }

            var map = nav.Map;
            map.TryFindRoomAt(transform.position, out var current);
            _candidateRooms.Clear();
            foreach (var candidate in map.Rooms)
            {
                if (current != null && candidate.Id == current.Id)
                {
                    continue;
                }

                // Patrol never chooses the spawn/evac room — only chase/noise can lead there.
                if (IsSpawnRoom(candidate))
                {
                    continue;
                }

                _candidateRooms.Add(candidate);
            }

            if (_candidateRooms.Count == 0)
            {
                return false;
            }

            room = _candidateRooms[Random.Range(0, _candidateRooms.Count)];
            return true;
        }

        void EnsureSpawnRoomCached()
        {
            if (_spawnRoomCached)
            {
                return;
            }

            _spawnRoomCached = true;
            _spawnRoomId = -1;
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                || !level.HasSpawnPoint
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return;
            }

            if (nav.Map.TryFindRoomAt(level.SpawnPosition, out var spawnRoom) && spawnRoom != null)
            {
                _spawnRoomId = spawnRoom.Id;
            }
        }

        bool IsSpawnRoom(RoomNavNode room)
        {
            return room != null && _spawnRoomId >= 0 && room.Id == _spawnRoomId;
        }

        bool IsInSpawnRoom(Vector3 worldPoint)
        {
            EnsureSpawnRoomCached();
            if (_spawnRoomId < 0
                || SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return false;
            }

            return nav.Map.TryFindRoomAt(worldPoint, out var room) && IsSpawnRoom(room);
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

        void SetPathTo(Vector3 destination)
        {
            ClearPath();
            destination.y = transform.position.y;
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                && nav.TryFindPath(transform.position, destination, _waypoints)
                && _waypoints.Count > 0)
            {
                _hasPath = true;
                _waypointIndex = 0;
                return;
            }

            _waypoints.Add(destination);
            _hasPath = true;
            _waypointIndex = 0;
        }

        void TickPathMovement(float speed)
        {
            if (!_hasPath || _waypointIndex >= _waypoints.Count)
            {
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
                    ClearPath();
                }

                return;
            }

            MoveTowards(target, speed);
            FacePoint(target);
        }

        void MoveTowards(Vector3 target, float speed)
        {
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;
            if (distance < 0.0001f)
            {
                return;
            }

            var step = Mathf.Min(speed * Runner.DeltaTime, distance);
            transform.position += toTarget / distance * step;
        }

        void FacePoint(Vector3 worldPoint)
        {
            var dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Runner.DeltaTime);
        }

        void ClearPath()
        {
            _waypoints.Clear();
            _waypointIndex = 0;
            _hasPath = false;
        }

        void SetVisionActive(bool active)
        {
            if (_visionCone != null)
            {
                _visionCone.gameObject.SetActive(active);
            }
        }

        void RefreshStunVisual(bool stunned)
        {
            if (_stunStars != null)
            {
                _stunStars.SetActive(stunned);
            }
        }

        void EnsureHitCollider()
        {
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = gameObject.AddComponent<CapsuleCollider>();
            }

            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = 0.4f;
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
        }
    }
}
