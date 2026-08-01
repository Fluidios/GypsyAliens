using System.Collections.Generic;
using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Host-authoritative fearful animal: wander in room, flee, stun, and optional player drag.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkFearfulNpc : NetworkBehaviour
    {
        enum AiState
        {
            Wander = 0,
            Flee = 1,
            Stunned = 2,
            Dragged = 3,
            Extracting = 4,
        }

        [SerializeField] float _walkSpeed = 0.85f;
        [SerializeField] float _fleeSpeed = 2.6f;
        [SerializeField] float _turnSpeed = 420f;
        [SerializeField] float _arriveDistance = 0.3f;
        [SerializeField] float _visionRange = 8f;
        [SerializeField] float _visionNearRadius = 2.5f;
        [SerializeField] float _visionAngle = 70f;
        [SerializeField] float _eyeHeight = 0.4f;
        [SerializeField] float _detectInterval = 0.15f;
        [SerializeField] float _wanderPauseMin = 0.8f;
        [SerializeField] float _wanderPauseMax = 2.4f;
        [SerializeField] float _roomMargin = 0.7f;
        [SerializeField] float _stunDuration = 10f;
        [SerializeField] float _dragFollowDistance = 1.15f;
        [SerializeField] float _dragWeight = 0.45f;
        [SerializeField] float _dragNoiseRadius = 3.2f;
        [SerializeField] float _dragNoiseLoudness = 0.75f;
        [SerializeField] float _extractDuration = 1.35f;
        [SerializeField] float _extractLiftHeight = 8f;
        [SerializeField] VisionConeView _visionCone;
        [SerializeField] CharacterController _characterController;
        [SerializeField] StunStarsEffect _stunStars;
        [SerializeField] DragOutlineView _dragOutline;
        [SerializeField] DragNoiseSource _dragNoise;
        [SerializeField] AudioClip _dragStartSfx;
        [SerializeField] AudioClip _extractSfx;
        [SerializeField] [Range(0f, 1f)] float _sfxVolume = 1f;

        readonly List<Vector3> _waypoints = new List<Vector3>(8);
        readonly List<RoomNavNode> _candidateRooms = new List<RoomNavNode>(16);
        readonly List<NetworkPlayerController> _dragOwners = new List<NetworkPlayerController>(4);

        AiState _state;
        int _waypointIndex;
        bool _hasPath;
        float _detectTimer;
        float _wanderPauseLeft;
        float _stunLeft;
        bool _stunFromDrag;
        float _extractElapsed;
        Vector3 _extractStart;
        float _extractTargetY;
        bool _extractFinished;
        bool _extractFxPlayed;
        bool _wasDragged;
        GypsyAliens.Gameplay.EvacuationZone _extractZone;
        CapsuleCollider _hitCollider;

        [Networked] public NetworkBool IsStunned { get; set; }
        [Networked] public NetworkBool IsDragged { get; set; }
        [Networked] public NetworkBool IsExtracting { get; set; }
        [Networked] public NetworkBool IsMakingDragNoise { get; set; }
        [Networked] public int DragOwnerCount { get; set; }

        /// <summary>Heavier animals (dog) slow carriers more. Cat ~0.4, dog ~1.15.</summary>
        public float DragWeight => Mathf.Max(0.05f, _dragWeight);

        public bool IsAvailableForDrag =>
            Object != null && Object.IsValid
            && !IsDragged
            && !IsExtracting
            && _state != AiState.Flee
            && _state != AiState.Extracting;

        public bool CanAcceptDrag(NetworkPlayerController player)
        {
            if (player == null || Object == null || !Object.IsValid || IsExtracting)
            {
                return false;
            }

            if (_state == AiState.Flee || _state == AiState.Extracting)
            {
                return false;
            }

            if (_dragOwners.Contains(player))
            {
                return true;
            }

            if (!IsDragged)
            {
                return true;
            }

            return _dragOwners.Count < 4;
        }

        /// <summary>
        /// Move speed factor while dragging this animal (shared across helpers).
        /// </summary>
        public float GetDragSpeedFactor()
        {
            var helpers = Mathf.Max(1, DragOwnerCount > 0 ? DragOwnerCount : _dragOwners.Count);
            return Mathf.Clamp(helpers / (1f + DragWeight), 0.28f, 1.05f);
        }

        public override void Spawned()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            // Movement is transform + NetworkTransform. Keep a dedicated hit capsule so rocks
            // can still OverlapSphere-stun after CharacterController is disabled.
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            EnsureHitCollider();

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
                    _stunStars = starsGo.AddComponent<StunStarsEffect>();
                }
            }

            if (_dragOutline == null)
            {
                _dragOutline = GetComponentInChildren<DragOutlineView>(true);
                if (_dragOutline == null)
                {
                    var outlineGo = new GameObject("DragOutline");
                    outlineGo.transform.SetParent(transform, false);
                    _dragOutline = outlineGo.AddComponent<DragOutlineView>();
                }
            }

            if (_dragNoise == null)
            {
                _dragNoise = GetComponentInChildren<DragNoiseSource>(true);
                if (_dragNoise == null)
                {
                    _dragNoise = gameObject.AddComponent<DragNoiseSource>();
                }
            }

            _dragNoise.SetRadius(_dragNoiseRadius, _dragNoiseLoudness);
            _dragNoise.SetEmitting(false);

            if (_visionCone != null)
            {
                _visionCone.Configure(_visionRange, _visionNearRadius, _visionAngle);
            }

            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(0.2f, 1f);
            ClearPath();
            RefreshStunVisual(false);
            RefreshDragOutline(false);
            SetVisionActive(true);
        }

        public override void Render()
        {
            if (Object == null || !Object.IsValid)
            {
                return;
            }

            RefreshStunVisual(IsStunned && !IsExtracting);
            RefreshDragOutline(IsDragged && !IsExtracting);
            SetVisionActive(!IsStunned && !IsDragged && !IsExtracting);
            if (_dragNoise != null)
            {
                _dragNoise.SetRadius(_dragNoiseRadius, _dragNoiseLoudness);
                _dragNoise.SetEmitting(IsMakingDragNoise && IsDragged && !IsExtracting);
            }

            // Optional per-animal SFX (parrot only) — play locally on every peer.
            var dragged = IsDragged && !IsExtracting;
            if (dragged && !_wasDragged)
            {
                PlayLocalSfx(_dragStartSfx);
            }

            _wasDragged = dragged;

            // Saucer / beam are local visuals — play on every peer when extraction starts.
            if (IsExtracting)
            {
                if (!_extractFxPlayed)
                {
                    _extractFxPlayed = true;
                    PlayLocalExtractionFx(transform.position);
                    PlayLocalSfx(_extractSfx);
                }
            }
            else
            {
                _extractFxPlayed = false;
            }
        }

        void PlayLocalSfx(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            // 2D one-shot so short animal cues stay audible regardless of camera distance.
            var go = new GameObject("NpcSfx");
            go.transform.position = transform.position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = Mathf.Clamp01(_sfxVolume);
            source.spatialBlend = 0f;
            source.Play();
            Destroy(go, clip.length + 0.25f);
        }

        static void PlayLocalExtractionFx(Vector3 from)
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<GypsyAliens.Gameplay.EvacuationZoneSystem>(out var evac)
                && evac.Zone != null)
            {
                evac.Zone.PlayExtractionEffect(from);
                return;
            }

            var zone = FindFirstObjectByType<GypsyAliens.Gameplay.EvacuationZone>();
            zone?.PlayExtractionEffect(from);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            // Prefer networked flag — local _state can desync during Fusion resimulation
            // after CharacterController was disabled for extraction.
            if (IsExtracting || _state == AiState.Extracting)
            {
                _state = AiState.Extracting;
                TickExtracting();
                return;
            }

            if (_state == AiState.Stunned)
            {
                TickStunned();
                return;
            }

            if (_state == AiState.Dragged)
            {
                TickDragged();
                return;
            }

            _detectTimer -= Runner.DeltaTime;
            if (_detectTimer <= 0f)
            {
                _detectTimer = _detectInterval;
                if (_state == AiState.Wander || _state == AiState.Flee)
                {
                    if (TryDetectPlayer(out var player))
                    {
                        BeginFlee(player.position, avoidPlayerRooms: true);
                    }
                    else if (NoiseRegistry.TryGetAudible(
                                 transform.position,
                                 out var noisePos,
                                 out _,
                                 ignoreRoot: transform))
                    {
                        // Flee away from the noise — can be herded toward the thrower.
                        BeginFlee(noisePos, avoidPlayerRooms: false);
                    }
                }
            }

            if (_state == AiState.Flee)
            {
                TickPathMovement(_fleeSpeed, returnToWander: true);
            }
            else if (_state == AiState.Wander)
            {
                TickWander();
            }
        }

        public void ApplyStun(float duration = -1f)
        {
            if (!HasStateAuthority || _state == AiState.Extracting)
            {
                return;
            }

            ClearAllDragOwners();
            _stunFromDrag = false;
            _stunLeft = duration > 0f ? duration : _stunDuration;
            _state = AiState.Stunned;
            IsStunned = true;
            IsDragged = false;
            DragOwnerCount = 0;
            ClearPath();
            SetVisionActive(false);
            RefreshStunVisual(true);
            RefreshDragOutline(false);
            SetDragNoise(false);
        }

        public void BeginDrag(NetworkPlayerController owner)
        {
            if (!HasStateAuthority || owner == null || _state == AiState.Extracting)
            {
                return;
            }

            if (!CanAcceptDrag(owner))
            {
                return;
            }

            if (!_dragOwners.Contains(owner))
            {
                _dragOwners.Add(owner);
            }

            DragOwnerCount = _dragOwners.Count;
            _state = AiState.Dragged;
            IsDragged = true;
            ClearPath();
            SetIgnoreCollisionWithPlayer(owner, ignore: true);

            // Drag always keeps the animal stunned until release (sneak-grab or already rocked).
            if (!IsStunned || _stunLeft <= 0f)
            {
                _stunFromDrag = true;
                _stunLeft = 0f;
            }

            IsStunned = true;
            SetVisionActive(false);
            RefreshStunVisual(true);
            RefreshDragOutline(true);
        }

        public void EndDrag()
        {
            EndDrag(null);
        }

        public void EndDrag(NetworkPlayerController owner)
        {
            if (!HasStateAuthority)
            {
                return;
            }

            if (owner != null)
            {
                SetIgnoreCollisionWithPlayer(owner, ignore: false);
                _dragOwners.Remove(owner);
            }
            else
            {
                ClearAllDragOwners();
            }

            DragOwnerCount = _dragOwners.Count;

            if (_state == AiState.Extracting)
            {
                IsDragged = _dragOwners.Count > 0;
                RefreshDragOutline(IsDragged);
                if (_dragOwners.Count == 0)
                {
                    SetDragNoise(false);
                }

                return;
            }

            if (_dragOwners.Count > 0)
            {
                IsDragged = true;
                RefreshDragOutline(true);
                return;
            }

            IsDragged = false;
            RefreshDragOutline(false);
            SetDragNoise(false);

            if (_state != AiState.Dragged)
            {
                return;
            }

            // Rock stun still running — resume stunned idle.
            if (!_stunFromDrag && _stunLeft > 0f)
            {
                _state = AiState.Stunned;
                IsStunned = true;
                SetVisionActive(false);
                RefreshStunVisual(true);
                return;
            }

            // Drag-only stun ends on release.
            _stunFromDrag = false;
            _stunLeft = 0f;
            IsStunned = false;
            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
            SetVisionActive(true);
            RefreshStunVisual(false);
        }

        /// <summary>
        /// Abduct the animal into the evacuation saucer (host-only).
        /// </summary>
        public void BeginExtraction(GypsyAliens.Gameplay.EvacuationZone zone)
        {
            if (!HasStateAuthority || IsExtracting || _state == AiState.Extracting)
            {
                return;
            }

            if (_dragOwners.Count > 0)
            {
                for (var i = 0; i < _dragOwners.Count; i++)
                {
                    var owner = _dragOwners[i];
                    if (owner != null)
                    {
                        owner.ClearDraggedNpc(this);
                        SetIgnoreCollisionWithPlayer(owner, ignore: false);
                    }
                }

                _dragOwners.Clear();
            }

            DragOwnerCount = 0;
            IsDragged = false;
            RefreshDragOutline(false);
            SetDragNoise(false);
            ClearPath();

            _state = AiState.Extracting;
            IsExtracting = true;
            IsStunned = true;
            SetVisionActive(false);
            RefreshStunVisual(false);

            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            if (_hitCollider != null)
            {
                _hitCollider.enabled = false;
            }

            // Stop occlusion / noise VFX from chasing a rising/despawning body.
            var silhouette = GetComponent<GypsyAliens.Rendering.OcclusionSilhouette>();
            if (silhouette != null)
            {
                silhouette.enabled = false;
            }

            _extractZone = zone;
            _extractStart = transform.position;
            _extractElapsed = 0f;
            _extractFinished = false;
            _extractTargetY = zone != null
                ? zone.SaucerIntakeHeight
                : _extractStart.y + Mathf.Max(2f, _extractLiftHeight * 0.5f);
            // Visual saucer move is handled in Render on all peers via IsExtracting.
        }

        void TickExtracting()
        {
            if (_extractFinished)
            {
                return;
            }

            _extractElapsed += Runner.DeltaTime;
            var duration = Mathf.Max(0.2f, _extractDuration);
            var t = Mathf.Clamp01(_extractElapsed / duration);

            // Rise into the saucer underside, then disappear — do not fly past it.
            var next = _extractStart;
            next.y = Mathf.Lerp(_extractStart.y, _extractTargetY, t * t);
            transform.position = next;

            if (t < 1f)
            {
                return;
            }

            _extractFinished = true;
            HideRenderersForAbduction();

            var session = NetworkGameSession.Instance;
            if (session != null)
            {
                session.NotifyAnimalExtracted();
            }

            if (Runner != null && Object != null && Object.IsValid)
            {
                Runner.Despawn(Object);
            }
        }

        void HideRenderersForAbduction()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = false;
                }
            }
        }

        void TickStunned()
        {
            if (_state == AiState.Extracting)
            {
                return;
            }

            _stunLeft -= Runner.DeltaTime;
            if (_stunLeft > 0f)
            {
                return;
            }

            IsStunned = false;
            _state = AiState.Wander;
            _wanderPauseLeft = Random.Range(0.4f, 1.2f);
            SetVisionActive(true);
            RefreshStunVisual(false);
        }

        void TickDragged()
        {
            PruneInvalidDragOwners();
            if (_dragOwners.Count == 0)
            {
                EndDrag(null);
                return;
            }

            DragOwnerCount = _dragOwners.Count;

            var centroid = Vector3.zero;
            var forward = Vector3.zero;
            var anyMoving = false;
            for (var i = 0; i < _dragOwners.Count; i++)
            {
                var owner = _dragOwners[i];
                centroid += owner.transform.position;
                forward += owner.transform.forward;
                if (owner.IsActivelyMoving)
                {
                    anyMoving = true;
                }
            }

            centroid /= _dragOwners.Count;
            SetDragNoise(anyMoving);

            var toNpc = transform.position - centroid;
            toNpc.y = 0f;
            var dist = toNpc.magnitude;
            if (dist < 0.001f)
            {
                toNpc = -forward;
                toNpc.y = 0f;
                if (toNpc.sqrMagnitude < 0.001f)
                {
                    toNpc = Vector3.right;
                }

                dist = toNpc.magnitude;
            }

            if (dist > _dragFollowDistance)
            {
                var dir = toNpc / dist;
                var target = centroid + dir * _dragFollowDistance;
                target.y = transform.position.y;
                var speed = _fleeSpeed * GetDragSpeedFactor();
                MoveTowards(target, speed);
            }
        }

        void ClearAllDragOwners()
        {
            for (var i = 0; i < _dragOwners.Count; i++)
            {
                var owner = _dragOwners[i];
                if (owner != null)
                {
                    owner.ClearDraggedNpc(this);
                    SetIgnoreCollisionWithPlayer(owner, ignore: false);
                }
            }

            _dragOwners.Clear();
            DragOwnerCount = 0;
        }

        void PruneInvalidDragOwners()
        {
            for (var i = _dragOwners.Count - 1; i >= 0; i--)
            {
                var owner = _dragOwners[i];
                if (owner == null || !owner.Object || !owner.Object.IsValid)
                {
                    _dragOwners.RemoveAt(i);
                }
            }
        }

        void SetDragNoise(bool emitting)
        {
            IsMakingDragNoise = emitting;
            if (_dragNoise == null)
            {
                return;
            }

            _dragNoise.SetRadius(_dragNoiseRadius, _dragNoiseLoudness);
            _dragNoise.SetEmitting(emitting);
        }

        void TickWander()
        {
            if (_hasPath)
            {
                TickPathMovement(_walkSpeed, returnToWander: false);
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
                _waypoints.Clear();
                _waypoints.Add(point);
                _hasPath = true;
                _waypointIndex = 0;
            }
            else
            {
                _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
            }
        }

        bool TryPickWanderPoint(out Vector3 point)
        {
            point = transform.position;
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                || !nav.HasMap)
            {
                return false;
            }

            if (!nav.Map.TryFindRoomAt(transform.position, out var room)
                && !TryNearestRoom(nav.Map, transform.position, out room))
            {
                return false;
            }

            var b = room.Bounds;
            var x = Random.Range(b.xMin + _roomMargin, b.xMax - _roomMargin);
            var z = Random.Range(b.yMin + _roomMargin, b.yMax - _roomMargin);
            point = new Vector3(x, transform.position.y, z);
            return true;
        }

        bool TryDetectPlayer(out Transform player)
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

                player = p.transform;
                return true;
            }

            return false;
        }

        void BeginFlee(Vector3 threatWorld, bool avoidPlayerRooms)
        {
            if (!TryPickFleeRoom(threatWorld, avoidPlayerRooms, out var room))
            {
                return;
            }

            var destination = room.Center;
            // Bias destination slightly away from the threat inside the target room.
            var away = destination - threatWorld;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
            {
                away.Normalize();
                destination += away * 1.2f;
            }

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
                ClearAllDragOwners();
                IsDragged = false;
                SetDragNoise(false);
                RefreshDragOutline(false);
            }
        }

        bool TryPickFleeRoom(Vector3 threatWorld, bool avoidPlayerRooms, out RoomNavNode room)
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

            HashSet<int> occupied = null;
            if (avoidPlayerRooms)
            {
                CollectOccupiedRoomIds(map, out occupied);
            }

            _candidateRooms.Clear();
            foreach (var door in current.Doors)
            {
                if (!map.TryGetRoom(door.ToRoomId, out var neighbor))
                {
                    continue;
                }

                if (neighbor.Id == current.Id)
                {
                    continue;
                }

                if (occupied != null && occupied.Contains(neighbor.Id))
                {
                    continue;
                }

                _candidateRooms.Add(neighbor);
            }

            if (_candidateRooms.Count == 0)
            {
                foreach (var candidate in map.Rooms)
                {
                    if (candidate.Id == current.Id)
                    {
                        continue;
                    }

                    if (occupied != null && occupied.Contains(candidate.Id))
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

            // Prefer the room farthest from the threat (away from player / rock noise).
            room = _candidateRooms[0];
            var best = -1f;
            for (var i = 0; i < _candidateRooms.Count; i++)
            {
                var c = _candidateRooms[i].Center;
                var dx = c.x - threatWorld.x;
                var dz = c.z - threatWorld.z;
                var d = dx * dx + dz * dz;
                if (d > best)
                {
                    best = d;
                    room = _candidateRooms[i];
                }
            }

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

        void TickPathMovement(float speed, bool returnToWander)
        {
            if (!_hasPath || _waypointIndex >= _waypoints.Count)
            {
                ClearPath();
                if (returnToWander)
                {
                    _state = AiState.Wander;
                    _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
                }

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
                    if (returnToWander)
                    {
                        _state = AiState.Wander;
                        _wanderPauseLeft = Random.Range(_wanderPauseMin, _wanderPauseMax);
                    }
                }

                return;
            }

            MoveTowards(target, speed);
            FaceDirection(toTarget / distance);
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
            var delta = toTarget / distance * step;
            transform.position += delta;
        }

        void FaceDirection(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
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

        void RefreshDragOutline(bool dragged)
        {
            if (_dragOutline != null)
            {
                _dragOutline.SetVisible(dragged);
            }
        }

        void EnsureHitCollider()
        {
            _hitCollider = GetComponent<CapsuleCollider>();
            if (_hitCollider == null)
            {
                _hitCollider = gameObject.AddComponent<CapsuleCollider>();
            }

            // Generous hit volume so thrown rocks can stun small visuals (scaled cat).
            // Must be a trigger — solid capsules shove the player's CharacterController through walls while dragging.
            var radius = 0.35f;
            var height = 0.75f;
            var centerY = 0.35f;
            if (_characterController != null)
            {
                radius = Mathf.Max(0.28f, _characterController.radius * 2.5f);
                height = Mathf.Max(radius * 2f + 0.05f, _characterController.height * 1.5f);
                centerY = Mathf.Max(height * 0.5f, _characterController.center.y);
            }

            _hitCollider.isTrigger = true;
            _hitCollider.direction = 1;
            _hitCollider.radius = radius;
            _hitCollider.height = height;
            _hitCollider.center = new Vector3(0f, centerY, 0f);
        }

        void SetIgnoreCollisionWithPlayer(NetworkPlayerController player, bool ignore)
        {
            if (player == null)
            {
                return;
            }

            var playerCc = player.GetComponent<CharacterController>();
            if (playerCc == null)
            {
                return;
            }

            if (_hitCollider != null)
            {
                Physics.IgnoreCollision(_hitCollider, playerCc, ignore);
            }

            if (_characterController != null)
            {
                Physics.IgnoreCollision(_characterController, playerCc, ignore);
            }
        }
    }
}
