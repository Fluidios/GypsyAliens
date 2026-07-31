using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Npc;
using GypsyAliens.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Spawns players/session and feeds local click-to-move / throw / drag input into Fusion.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] NetworkObject _sessionPrefab;
        [SerializeField] ClickRippleEffect _clickRipplePrefab;
        [SerializeField] float _clickRayLength = 500f;
        [SerializeField] float _dragRange = 2.2f;

        readonly Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        NetworkObject _sessionInstance;
        LayerMask _floorMask;

        bool _hasPendingClick;
        Vector3 _pendingClickWorld;

        bool _throwAiming;
        bool _dragHolding;
        bool _throwReleasedThisFrame;
        bool _throwPathClear;
        Vector3 _aimPoint;
        PlayerTutorialHints _tutorial;

        public void Configure(NetworkObject playerPrefab, NetworkObject sessionPrefab)
        {
            if (playerPrefab != null)
            {
                _playerPrefab = playerPrefab;
            }

            if (sessionPrefab != null)
            {
                _sessionPrefab = sessionPrefab;
            }
        }

        void Awake()
        {
            _floorMask = GameLayers.FloorMask;
            var runner = GetComponent<NetworkRunner>();
            if (runner != null)
            {
                runner.AddCallbacks(this);
            }

            EnsureTutorial();
        }

        void EnsureTutorial()
        {
            if (_tutorial != null)
            {
                return;
            }

            _tutorial = GetComponent<PlayerTutorialHints>();
            if (_tutorial == null)
            {
                _tutorial = gameObject.AddComponent<PlayerTutorialHints>();
            }
        }

        void Update()
        {
            EnsureTutorial();

            var paused = SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<PauseMenuSystem>(out var pause)
                && pause.IsOpen;

            var hasLocal = TryGetLocalPlayer(out var localPlayer);
            var gameplayReady = NetworkGameSession.Instance != null && NetworkGameSession.Instance.GameplayReady;
            _tutorial?.SetActive(hasLocal && gameplayReady && !paused);

            if (paused)
            {
                // Keep local input idle while the overlay is up; simulation continues on the host.
                _hasPendingClick = false;
                _throwAiming = false;
                _dragHolding = false;
                _throwReleasedThisFrame = false;
                if (localPlayer != null && localPlayer.TryGetComponent<PlayerThrowAimView>(out var aimView))
                {
                    aimView.SetVisible(false);
                }

                return;
            }

            if (TryCaptureClick(out var worldPoint))
            {
                _hasPendingClick = true;
                _pendingClickWorld = worldPoint;
                ClickRippleEffect.Play(worldPoint + Vector3.up * 0.05f, _clickRipplePrefab);
                _tutorial?.NotifyMoveClicked();
            }

            UpdateSpaceActions();
        }

        void UpdateSpaceActions()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            TryGetLocalPlayer(out var localPlayer);
            TryGetAimPoint(out _aimPoint);

            var nearNpc = localPlayer != null
                && !localPlayer.IsStunned
                && TryFindDragTarget(localPlayer.transform.position, out _);
            _tutorial?.NotifyNearAnimal(nearNpc);

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                if (nearNpc)
                {
                    _dragHolding = true;
                    _throwAiming = false;
                    _tutorial?.NotifyDragStarted();
                }
                else if (localPlayer == null || !localPlayer.IsStunned)
                {
                    _throwAiming = true;
                    _dragHolding = false;
                }
            }

            // Local parabolic aim preview while holding Space (no charge).
            _throwPathClear = false;
            if (localPlayer != null && localPlayer.TryGetComponent<PlayerThrowAimView>(out var aimView))
            {
                if (_throwAiming && !localPlayer.IsStunned)
                {
                    _throwPathClear = aimView.UpdateAim(localPlayer.transform.position, _aimPoint);
                }
                else
                {
                    aimView.SetVisible(false);
                }
            }
            else if (_throwAiming)
            {
                // Aim view missing — still allow throws (no false "cooldown").
                _throwPathClear = true;
            }

            if (keyboard.spaceKey.wasReleasedThisFrame)
            {
                if (_throwAiming && _throwPathClear && (localPlayer == null || !localPlayer.IsStunned))
                {
                    // Sticky until OnInput consumes it (Fusion may poll OnInput before Update).
                    _throwReleasedThisFrame = true;
                    _tutorial?.NotifyRockThrown();
                }

                _throwAiming = false;
                _dragHolding = false;
            }
        }

        bool TryGetLocalPlayer(out NetworkPlayerController player)
        {
            player = null;
            var runner = GetComponent<NetworkRunner>();
            if (runner == null || !runner.IsRunning)
            {
                return false;
            }

            foreach (var kv in _spawnedPlayers)
            {
                if (kv.Key != runner.LocalPlayer || kv.Value == null)
                {
                    continue;
                }

                if (kv.Value.TryGetComponent(out player))
                {
                    return true;
                }
            }

            // Clients: local player may not be in _spawnedPlayers dictionary (server-only fill).
            var all = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].HasInputAuthority)
                {
                    player = all[i];
                    return true;
                }
            }

            return false;
        }

        bool TryFindDragTarget(Vector3 from, out NetworkFearfulNpc npc)
        {
            npc = null;
            var best = _dragRange * _dragRange;
            var animals = FindObjectsByType<NetworkFearfulNpc>(FindObjectsSortMode.None);
            for (var i = 0; i < animals.Length; i++)
            {
                var a = animals[i];
                if (a == null || a.Object == null || !a.Object.IsValid || a.IsExtracting)
                {
                    continue;
                }

                // Available to start a drag, or already dragged (helpers can join).
                if (!a.IsAvailableForDrag && !a.IsDragged)
                {
                    continue;
                }

                var d = a.transform.position - from;
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

        bool TryGetAimPoint(out Vector3 point)
        {
            point = default;
            var mouse = Mouse.current;
            var cam = UnityEngine.Camera.main;
            if (mouse == null || cam == null)
            {
                return false;
            }

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, _clickRayLength, _floorMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }

            // Fallback plane at y=0.
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out var enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }

            return false;
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer)
            {
                return;
            }

            EnsureSession(runner);

            if (_playerPrefab == null)
            {
                Debug.LogError("PlayerSpawner: player prefab is not assigned.");
                return;
            }

            var spawnPos = Vector3.zero;
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level)
                && level.IsReady
                && level.HasSpawnPoint)
            {
                spawnPos = level.SpawnPosition;
            }

            var obj = runner.Spawn(_playerPrefab, spawnPos, Quaternion.identity, player);
            _spawnedPlayers[player] = obj;
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (_spawnedPlayers.TryGetValue(player, out var obj))
            {
                runner.Despawn(obj);
                _spawnedPlayers.Remove(player);
            }
        }

        void EnsureSession(NetworkRunner runner)
        {
            if (_sessionInstance != null || _sessionPrefab == null || !runner.IsServer)
            {
                return;
            }

            _sessionInstance = runner.Spawn(_sessionPrefab, Vector3.zero, Quaternion.identity);
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new NetworkPlayerInput();

            if (_hasPendingClick)
            {
                data.MoveTarget = new Vector2(_pendingClickWorld.x, _pendingClickWorld.z);
                data.SetMoveTarget = true;
                _hasPendingClick = false;
            }

            data.SpaceHeld = _dragHolding && Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
            data.ThrowReleased = _throwReleasedThisFrame;
            data.AimPoint = new Vector2(_aimPoint.x, _aimPoint.z);

            if (_throwReleasedThisFrame)
            {
                _throwReleasedThisFrame = false;
            }

            input.Set(data);
        }

        bool TryCaptureClick(out Vector3 worldPoint)
        {
            worldPoint = default;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return false;
            }

            if (IsPointerOverUi())
            {
                return false;
            }

            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                return false;
            }

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, _clickRayLength, _floorMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            worldPoint = hit.point;
            return true;
        }

        bool IsPointerOverUi()
        {
            var es = EventSystem.current;
            if (es == null || Mouse.current == null)
            {
                return false;
            }

            var eventData = new PointerEventData(es)
            {
                position = Mouse.current.position.ReadValue(),
            };
            _uiHits.Clear();
            es.RaycastAll(eventData, _uiHits);
            return _uiHits.Count > 0;
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
