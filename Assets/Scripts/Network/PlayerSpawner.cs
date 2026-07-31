using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Spawns players/session and feeds local click-to-move input into Fusion.
    /// Lives on the NetworkRunner prefab.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] NetworkObject _sessionPrefab;
        [SerializeField] ClickRippleEffect _clickRipplePrefab;
        [SerializeField] float _clickRayLength = 500f;

        readonly Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        NetworkObject _sessionInstance;
        LayerMask _floorMask;

        // Buffered in Update — Fusion OnInput can miss wasPressedThisFrame across tick timing.
        bool _hasPendingClick;
        Vector3 _pendingClickWorld;

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
        }

        void Update()
        {
            if (!TryCaptureClick(out var worldPoint))
            {
                return;
            }

            _hasPendingClick = true;
            _pendingClickWorld = worldPoint;
            ClickRippleEffect.Play(worldPoint + Vector3.up * 0.05f, _clickRipplePrefab);
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
            // NetworkPlayerController.Spawned holds/releases when the level is ready.
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
            // Floor-only: walls never block click targeting (isometric occlusion).
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
            if (es == null)
            {
                return false;
            }

            // Explicit raycast — IsPointerOverGameObject() is unreliable with the new Input System.
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
