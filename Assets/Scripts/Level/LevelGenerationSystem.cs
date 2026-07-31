using System;
using System.Collections;
using GypsyAliens.Core;
using UnityEngine;

namespace GypsyAliens.Level
{
    /// <summary>
    /// System facade for procedural level generation.
    /// </summary>
    public sealed class LevelGenerationSystem : GameSystemBehaviour<LevelGenerationSystem>
    {
        [SerializeField] ProceduralBuildingGenerator _generator;
        [SerializeField] BuildingTileSet _tileSet;
        [SerializeField] Transform _levelRoot;

        Coroutine _generateRoutine;

        public Vector3 SpawnPosition => _generator != null ? _generator.SpawnPosition : Vector3.zero;
        public bool HasSpawnPoint => _generator != null && _generator.HasSpawnPoint;
        public bool IsReady => _generator != null && _generator.IsReady;
        public bool IsGenerating => _generator != null && _generator.IsGenerating;
        public LevelNavigationMap NavigationMap => _generator != null ? _generator.NavigationMap : null;

        public event Action GenerationStarted;
        public event Action LevelReady;

        protected override void Awake()
        {
            base.Awake();

            if (_generator == null)
            {
                _generator = GetComponent<ProceduralBuildingGenerator>();
                if (_generator == null)
                {
                    _generator = gameObject.AddComponent<ProceduralBuildingGenerator>();
                }
            }

            if (_tileSet != null)
            {
                _generator.SetTileSet(_tileSet);
            }

            if (_levelRoot != null)
            {
                _generator.SetLevelRoot(_levelRoot);
            }

            _generator.GenerationStarted += OnGeneratorStarted;
            _generator.LevelReady += OnGeneratorReady;
        }

        protected override void OnDestroy()
        {
            if (_generator != null)
            {
                _generator.GenerationStarted -= OnGeneratorStarted;
                _generator.LevelReady -= OnGeneratorReady;
            }

            base.OnDestroy();
        }

        void OnGeneratorStarted()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NavigationSystem>(out var nav))
            {
                nav.ClearMap();
            }

            GenerationStarted?.Invoke();
        }

        void OnGeneratorReady()
        {
            if (SystemLocator.Instance != null
                && SystemLocator.Instance.TryGet<NavigationSystem>(out var nav)
                && _generator.NavigationMap != null)
            {
                nav.SetMap(_generator.NavigationMap);
            }

            LevelReady?.Invoke();
        }

        public void Generate(int seed) => BeginGenerate(seed);

        public void BeginGenerate(int seed)
        {
            if (_generator == null)
            {
                Debug.LogError("LevelGenerationSystem: generator is missing.", this);
                return;
            }

            if (_generateRoutine != null)
            {
                StopCoroutine(_generateRoutine);
            }

            _generateRoutine = StartCoroutine(RunGenerate(seed));
        }

        IEnumerator RunGenerate(int seed)
        {
            yield return _generator.GenerateRoutine(seed);
            _generateRoutine = null;
        }
    }
}
