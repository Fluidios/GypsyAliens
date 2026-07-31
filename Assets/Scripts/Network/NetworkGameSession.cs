using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Gameplay;
using GypsyAliens.Level;
using UnityEngine;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Networked session state: level seed, gameplay ready, animal extraction mission, level complete.
    /// </summary>
    public sealed class NetworkGameSession : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnLevelSeedChanged))]
        public int LevelSeed { get; set; }

        [Networked, OnChangedRender(nameof(OnGameplayReadyChanged))]
        public NetworkBool GameplayReady { get; set; }

        [Networked, OnChangedRender(nameof(OnMissionChanged))]
        public int AnimalsRequired { get; set; }

        [Networked, OnChangedRender(nameof(OnMissionChanged))]
        public int AnimalsExtracted { get; set; }

        [Networked, OnChangedRender(nameof(OnMissionChanged))]
        public NetworkBool AnimalsObjectiveComplete { get; set; }

        [Networked, OnChangedRender(nameof(OnMissionChanged))]
        public NetworkBool LevelCompleted { get; set; }

        bool _generatedForSeed;

        public static NetworkGameSession Instance { get; private set; }

        public event System.Action GameplayReadyChanged;
        public event System.Action MissionChanged;

        public override void Spawned()
        {
            Instance = this;

            if (HasStateAuthority)
            {
                GameplayReady = false;
                AnimalsRequired = 0;
                AnimalsExtracted = 0;
                AnimalsObjectiveComplete = false;
                LevelCompleted = false;
                if (LevelSeed == 0)
                {
                    LevelSeed = UnityEngine.Random.Range(1, int.MaxValue);
                }
            }

            TryGenerate();
            NotifyGameplayReadyChanged();
            NotifyMissionChanged();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void MarkGameplayReady()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            GameplayReady = true;
        }

        public void SetAnimalsRequired(int count)
        {
            if (!HasStateAuthority)
            {
                return;
            }

            AnimalsRequired = Mathf.Max(0, count);
            AnimalsExtracted = 0;
            AnimalsObjectiveComplete = AnimalsRequired == 0;
            LevelCompleted = false;
            RefreshObjectiveFlags();
        }

        public void NotifyAnimalExtracted()
        {
            if (!HasStateAuthority || LevelCompleted)
            {
                return;
            }

            AnimalsExtracted = Mathf.Min(AnimalsExtracted + 1, Mathf.Max(AnimalsRequired, AnimalsExtracted + 1));
            RefreshObjectiveFlags();
        }

        public void TryCompleteLevelIfReady(EvacuationZone zone)
        {
            if (!HasStateAuthority || LevelCompleted || zone == null)
            {
                return;
            }

            RefreshObjectiveFlags();
            if (!AnimalsObjectiveComplete)
            {
                return;
            }

            if (!zone.AreAllPlayersInside())
            {
                return;
            }

            LevelCompleted = true;
        }

        /// <summary>
        /// Host-only: regenerate the level with a new seed and reset mission progress.
        /// </summary>
        public void RequestRestart()
        {
            if (!HasStateAuthority)
            {
                return;
            }

            LevelCompleted = false;
            GameplayReady = false;
            AnimalsRequired = 0;
            AnimalsExtracted = 0;
            AnimalsObjectiveComplete = false;
            _generatedForSeed = false;
            LevelSeed = UnityEngine.Random.Range(1, int.MaxValue);
        }

        void RefreshObjectiveFlags()
        {
            AnimalsObjectiveComplete = AnimalsRequired > 0 && AnimalsExtracted >= AnimalsRequired;
        }

        void OnLevelSeedChanged()
        {
            _generatedForSeed = false;
            if (HasStateAuthority)
            {
                GameplayReady = false;
                AnimalsRequired = 0;
                AnimalsExtracted = 0;
                AnimalsObjectiveComplete = false;
                LevelCompleted = false;
            }

            TryGenerate();
            NotifyMissionChanged();
        }

        void OnGameplayReadyChanged()
        {
            NotifyGameplayReadyChanged();
        }

        void OnMissionChanged()
        {
            NotifyMissionChanged();
        }

        void NotifyGameplayReadyChanged()
        {
            GameplayReadyChanged?.Invoke();
        }

        void NotifyMissionChanged()
        {
            MissionChanged?.Invoke();
        }

        void TryGenerate()
        {
            if (LevelSeed == 0 || _generatedForSeed)
            {
                return;
            }

            if (SystemLocator.Instance == null
                || !SystemLocator.Instance.TryGet<LevelGenerationSystem>(out var level))
            {
                Debug.LogWarning("NetworkGameSession: LevelGenerationSystem not available yet.");
                return;
            }

            level.BeginGenerate(LevelSeed);
            _generatedForSeed = true;
        }
    }
}
