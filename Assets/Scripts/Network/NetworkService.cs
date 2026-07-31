using System;
using System.Threading.Tasks;
using Fusion;
using GypsyAliens.Core;
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

        NetworkRunner _runner;

        public NetworkRunner Runner => _runner;
        public bool IsRunning => _runner != null && _runner.IsRunning;
        public NetworkObject SessionPrefab => _sessionPrefab;
        public NetworkObject PlayerPrefab => _playerPrefab;

        public Task StartHostAsync(string roomName) => StartGameAsync(GameMode.Host, roomName);

        public Task StartClientAsync(string roomName) => StartGameAsync(GameMode.Client, roomName);

        public Task StartAutoHostOrClientAsync(string roomName) =>
            StartGameAsync(GameMode.AutoHostOrClient, roomName);

        async Task StartGameAsync(GameMode mode, string roomName)
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

            _runner = Instantiate(_runnerPrefab);
            _runner.name = "NetworkRunner";
            DontDestroyOnLoad(_runner.gameObject);

            var spawner = _runner.GetComponent<PlayerSpawner>();
            if (spawner == null)
            {
                spawner = _runner.gameObject.AddComponent<PlayerSpawner>();
            }

            spawner.Configure(_playerPrefab, _sessionPrefab);

            var sceneManager = _runner.GetComponent<INetworkSceneManager>()
                               ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = string.IsNullOrWhiteSpace(roomName) ? null : roomName,
                SceneManager = sceneManager,
            };

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
        }

        public async Task ShutdownAsync()
        {
            if (_runner == null)
            {
                return;
            }

            await _runner.Shutdown();
            if (_runner != null)
            {
                Destroy(_runner.gameObject);
                _runner = null;
            }
        }
    }
}
