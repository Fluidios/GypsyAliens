using System.Collections.Generic;
using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Level;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Host spawns one cat and one dog into random rooms after the level is ready,
    /// then marks <see cref="NetworkGameSession.GameplayReady"/>.
    /// </summary>
    public sealed class NpcSpawner : GameSystemBehaviour<NpcSpawner>
    {
        [SerializeField] NetworkObject _catPrefab;
        [SerializeField] NetworkObject _dogPrefab;
        [SerializeField] float _spawnHeight = 0.05f;

        readonly List<NetworkObject> _spawned = new List<NetworkObject>(2);
        bool _spawnedForCurrentLevel;

        public void Configure(NetworkObject catPrefab, NetworkObject dogPrefab)
        {
            if (catPrefab != null)
            {
                _catPrefab = catPrefab;
            }

            if (dogPrefab != null)
            {
                _dogPrefab = dogPrefab;
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
            // Host may become ready slightly after LevelReady (runner / session timing).
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
                MarkReadyIfPossible();
                return;
            }

            var rooms = PickSpawnRooms(nav.Map, level.SpawnPosition, 2);
            if (rooms.Count < 2)
            {
                Debug.LogWarning("NpcSpawner: not enough rooms to place NPCs.", this);
                MarkReadyIfPossible();
                return;
            }

            DespawnAll();

            var runner = network.Runner;
            SpawnOne(runner, _catPrefab, rooms[0]);
            SpawnOne(runner, _dogPrefab, rooms[1]);

            _spawnedForCurrentLevel = true;
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

            // Fisher–Yates shuffle then take first N.
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
