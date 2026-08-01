using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using GypsyAliens.Core;
using GypsyAliens.UI;
using UnityEngine;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Starts Fusion sessions and exposes the active <see cref="NetworkRunner"/>.
    /// </summary>
    public sealed class NetworkService : GameSystemBehaviour<NetworkService>
    {
        [SerializeField] NetworkRunner _runnerPrefab;
        [SerializeField] NetworkObject _sessionPrefab;
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] NetworkObject _catNpcPrefab;
        [SerializeField] NetworkObject _dogNpcPrefab;
        [SerializeField] NetworkObject _parrotNpcPrefab;
        [SerializeField] NetworkObject _hostileNpcPrefab;
        [SerializeField] NetworkObject _rockPrefab;

        NetworkRunner _runner;
        bool _ownedShutdown;
        bool _returningToMenu;

        public NetworkRunner Runner => _runner;
        public bool IsRunning => _runner != null && _runner.IsRunning;
        public NetworkObject SessionPrefab => _sessionPrefab;
        public NetworkObject PlayerPrefab => _playerPrefab;
        public NetworkObject CatNpcPrefab => _catNpcPrefab;
        public NetworkObject DogNpcPrefab => _dogNpcPrefab;
        public NetworkObject ParrotNpcPrefab => _parrotNpcPrefab;
        public NetworkObject HostileNpcPrefab => _hostileNpcPrefab;
        public NetworkObject RockPrefab => _rockPrefab;

        public Task StartHostAsync(string roomName, string photonRegion = null) =>
            StartGameAsync(GameMode.Host, roomName, photonRegion);

        public Task StartClientAsync(string roomName, string photonRegion = null) =>
            StartGameAsync(GameMode.Client, roomName, photonRegion);

        public Task StartAutoHostOrClientAsync(string roomName, string photonRegion = null) =>
            StartGameAsync(GameMode.AutoHostOrClient, roomName, photonRegion);

        async Task StartGameAsync(GameMode mode, string roomName, string photonRegion)
        {
            if (_runnerPrefab == null)
            {
                throw new InvalidOperationException("NetworkService: runner prefab is not assigned.");
            }

            if (IsRunning)
            {
                Debug.LogWarning("NetworkService: session already running.");
                return;
            }

            _returningToMenu = false;
            _runner = Instantiate(_runnerPrefab);
            _runner.name = "NetworkRunner";
            DontDestroyOnLoad(_runner.gameObject);

            var spawner = _runner.GetComponent<PlayerSpawner>();
            if (spawner == null)
            {
                spawner = _runner.gameObject.AddComponent<PlayerSpawner>();
            }

            spawner.Configure(_playerPrefab, _sessionPrefab);

            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<GypsyAliens.Npc.NpcSpawner>(out var npcSpawner))
            {
                npcSpawner.Configure(
                    _catNpcPrefab,
                    _dogNpcPrefab,
                    _parrotNpcPrefab,
                    _hostileNpcPrefab);
            }

            var sceneManager = _runner.GetComponent<INetworkSceneManager>()
                               ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = string.IsNullOrWhiteSpace(roomName) ? null : roomName,
                SceneManager = sceneManager,
                CustomPhotonAppSettings = BuildAppSettings(photonRegion),
            };

            var regionLabel = string.IsNullOrWhiteSpace(photonRegion) ? "best" : photonRegion.Trim();
            Debug.Log($"NetworkService: starting {mode} room='{args.SessionName}' region='{regionLabel}'.");

            var result = await _runner.StartGame(args);
            if (result.Ok)
            {
                return;
            }

            Debug.LogError($"NetworkService: failed to start game: {result.ShutdownReason}");
            if (_runner != null)
            {
                Destroy(_runner.gameObject);
                _runner = null;
            }

            ReturnToConnectionMenu();
        }

        static FusionAppSettings BuildAppSettings(string photonRegion)
        {
            if (!PhotonAppSettings.TryGetGlobal(out var global) || global.AppSettings == null)
            {
                return null;
            }

            var settings = global.AppSettings.GetCopy();
            settings.FixedRegion = string.IsNullOrWhiteSpace(photonRegion)
                ? string.Empty
                : photonRegion.Trim().ToLowerInvariant();
            return settings;
        }

        public async Task ShutdownAsync()
        {
            if (_runner == null)
            {
                ReturnToConnectionMenu();
                return;
            }

            _ownedShutdown = true;
            var runner = _runner;
            try
            {
                await runner.Shutdown();
            }
            finally
            {
                _ownedShutdown = false;
                if (_runner == runner)
                {
                    _runner = null;
                }

                if (runner != null)
                {
                    Destroy(runner.gameObject);
                }
            }

            ReturnToConnectionMenu();
        }

        /// <summary>
        /// Called from Fusion callbacks when the runner stops (host left, kick, quit, etc.).
        /// </summary>
        public void NotifyRunnerStopped(NetworkRunner runner, string reason = null)
        {
            if (runner != null && _runner != null && runner != _runner)
            {
                return;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                Debug.Log($"NetworkService: session ended ({reason}).");
            }

            if (_runner == runner)
            {
                _runner = null;
            }

            // Owned ShutdownAsync destroys the runner after await — skip destroy here.
            if (!_ownedShutdown && runner != null)
            {
                Destroy(runner.gameObject);
            }

            ReturnToConnectionMenu();
        }

        /// <summary>
        /// Client lost the host / server — force local runner teardown if it is still marked running.
        /// </summary>
        public void NotifyDisconnectedFromServer(NetworkRunner runner, string reason = null)
        {
            if (runner != null && runner.IsRunning)
            {
                // Triggers OnShutdown → NotifyRunnerStopped.
                runner.Shutdown();
                return;
            }

            NotifyRunnerStopped(runner, reason ?? "DisconnectedFromServer");
        }

        void ReturnToConnectionMenu()
        {
            if (_returningToMenu)
            {
                return;
            }

            _returningToMenu = true;
            try
            {
                if (SystemLocator.Instance == null)
                {
                    return;
                }

                if (SystemLocator.Instance.TryGet<PauseMenuSystem>(out var pause))
                {
                    pause.SetOpen(false);
                }

                if (SystemLocator.Instance.TryGet<MissionProgressUISystem>(out var mission))
                {
                    mission.HideAllOverlays();
                }

                if (SystemLocator.Instance.TryGet<LoadingScreenSystem>(out var loading))
                {
                    loading.Hide();
                }

                if (SystemLocator.Instance.TryGet<ConnectionUISystem>(out var connection))
                {
                    connection.SetMenuVisible(true);
                }
            }
            finally
            {
                _returningToMenu = false;
            }
        }
    }
}
