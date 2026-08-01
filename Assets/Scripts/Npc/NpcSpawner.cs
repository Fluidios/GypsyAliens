using System.Collections.Generic;
using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Host spawns animals + one hostile homeowner after the level is ready,
    /// then marks <see cref="NetworkGameSession.GameplayReady"/>.
    /// </summary>
    public sealed class NpcSpawner : GameSystemBehaviour<NpcSpawner>
    {
        [SerializeField] NetworkObject _catPrefab;
        [SerializeField] NetworkObject _dogPrefab;
        [SerializeField] NetworkObject _parrotPrefab;
        [SerializeField] NetworkObject _hostilePrefab;
        [SerializeField] float _spawnHeight = 0.05f;

        readonly List<NetworkObject> _spawned = new List<NetworkObject>(4);
        bool _spawnedForCurrentLevel;

        public void Configure(
            NetworkObject catPrefab,
            NetworkObject dogPrefab,
            NetworkObject parrotPrefab = null,
            NetworkObject hostilePrefab = null)
        {
            if (catPrefab != null)
            {
                _catPrefab = catPrefab;
            }

            if (dogPrefab != null)
            {
                _dogPrefab = dogPrefab;
            }

            if (parrotPrefab != null)
            {
                _parrotPrefab = parrotPrefab;
            }

            if (hostilePrefab != null)
            {
                _hostilePrefab = hostilePrefab;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            BindLevel();
        }

        void Start()
        {
            BindLevel();
        }

        void Update()
        {
            if (!_spawnedForCurrentLevel)
            {
                TrySpawnNpcs();
            }
        }

        protected override void OnDestroy()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                level.GenerationStarted -= OnGenerationStarted;
                level.LevelReady -= OnLevelReady;
            }

            base.OnDestroy();
        }

        void BindLevel()
        {
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                return;
            }

            level.GenerationStarted -= OnGenerationStarted;
            level.LevelReady -= OnLevelReady;
            level.GenerationStarted += OnGenerationStarted;
            level.LevelReady += OnLevelReady;

            if (level.IsReady)
            {
                OnLevelReady();
            }
        }

        void OnGenerationStarted()
        {
            _spawnedForCurrentLevel = false;
            DespawnAll();
        }

        void OnLevelReady()
        {
            TrySpawnNpcs();
        }

        void TrySpawnNpcs()
        {
            if (_spawnedForCurrentLevel)
            {
                return;
            }

            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NetworkService>(out var network)
                || network.Runner == null
                || !network.Runner.IsRunning
                || !network.Runner.IsServer)
            {
                return;
            }

            if (!SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level) || !level.IsReady)
            {
                return;
            }

            if (!SystemLocator.Instance.TryGet<NavigationSystem>(out var nav) || !nav.HasMap)
            {
                return;
            }

            if (_catPrefab == null || _dogPrefab == null)
            {
                Debug.LogError("NpcSpawner: cat/dog prefabs are not assigned.", this);
                FinalizeSpawn(0);
                return;
            }

            var animalPrefabs = new List<NetworkObject>(3) { _catPrefab, _dogPrefab };
            if (_parrotPrefab != null)
            {
                animalPrefabs.Add(_parrotPrefab);
            }

            var neededRooms = animalPrefabs.Count + (_hostilePrefab != null ? 1 : 0);
            var rooms = PickSpawnRooms(nav.Map, level.SpawnPosition, neededRooms);
            if (rooms.Count < animalPrefabs.Count)
            {
                Debug.LogWarning("NpcSpawner: not enough rooms to place NPCs.", this);
                FinalizeSpawn(0);
                return;
            }

            DespawnAll();

            var runner = network.Runner;
            for (var i = 0; i < animalPrefabs.Count; i++)
            {
                SpawnOne(runner, animalPrefabs[i], rooms[i]);
            }

            var animalCount = _spawned.Count;
            if (_hostilePrefab != null)
            {
                var hostileRoom = rooms.Count > animalPrefabs.Count
                    ? rooms[animalPrefabs.Count]
                    : rooms[rooms.Count - 1];
                SpawnOne(runner, _hostilePrefab, hostileRoom);
            }

            FinalizeSpawn(animalCount);
        }

        void FinalizeSpawn(int animalCount)
        {
            _spawnedForCurrentLevel = true;
            var session = NetworkGameSession.Instance;
            if (session != null && session.HasStateAuthority)
            {
                session.SetAnimalsRequired(animalCount);
            }

            MarkReadyIfPossible();
        }

        void SpawnOne(NetworkRunner runner, NetworkObject prefab, RoomNavNode room)
        {
            var pos = room.Center;
            pos.y = _spawnHeight;
            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var obj = runner.Spawn(prefab, pos, rot, null);
            if (obj != null)
            {
                _spawned.Add(obj);
            }
        }

        void MarkReadyIfPossible()
        {
            var session = NetworkGameSession.Instance;
            if (session != null && session.HasStateAuthority)
            {
                session.MarkGameplayReady();
            }
        }

        void DespawnAll()
        {
            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<NetworkService>(out var network)
                || network.Runner == null)
            {
                _spawned.Clear();
                return;
            }

            var runner = network.Runner;
            for (var i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null && _spawned[i].IsValid)
                {
                    runner.Despawn(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        static List<RoomNavNode> PickSpawnRooms(LevelNavigationMap map, Vector3 playerSpawn, int count)
        {
            var result = new List<RoomNavNode>(count);
            var pool = new List<RoomNavNode>(map.Rooms.Count);
            RoomNavNode spawnRoom = null;
            map.TryFindRoomAt(playerSpawn, out spawnRoom);

            foreach (var room in map.Rooms)
            {
                if (spawnRoom != null && room.Id == spawnRoom.Id && map.Rooms.Count >= 3)
                {
                    continue;
                }

                pool.Add(room);
            }

            if (pool.Count == 0)
            {
                pool.AddRange(map.Rooms);
            }

            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            for (var i = 0; i < pool.Count && result.Count < count; i++)
            {
                result.Add(pool[i]);
            }

            return result;
        }
    }
}
