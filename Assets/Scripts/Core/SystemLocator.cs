using System;
using System.Collections.Generic;
using UnityEngine;

namespace GypsyAliens.Core
{
    /// <summary>
    /// Central registry for game systems. The only allowed root singleton.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SystemLocator : MonoBehaviour
    {
        public static SystemLocator Instance { get; private set; }

        readonly Dictionary<Type, IGameSystem> _systems = new Dictionary<Type, IGameSystem>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate SystemLocator destroyed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            _systems.Clear();
        }

        public void Register<T>(T system) where T : class, IGameSystem
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            var type = typeof(T);
            if (_systems.TryGetValue(type, out var existing) && !ReferenceEquals(existing, system))
            {
                Debug.LogWarning($"SystemLocator: replacing registered {type.Name}.", this);
            }

            _systems[type] = system;
        }

        public void Unregister<T>(T system) where T : class, IGameSystem
        {
            var type = typeof(T);
            if (_systems.TryGetValue(type, out var existing) && ReferenceEquals(existing, system))
            {
                _systems.Remove(type);
            }
        }

        public bool TryGet<T>(out T system) where T : class, IGameSystem
        {
            if (_systems.TryGetValue(typeof(T), out var registered) && registered is T typed)
            {
                system = typed;
                return true;
            }

            system = null;
            return false;
        }

        public T Get<T>() where T : class, IGameSystem
        {
            if (TryGet<T>(out var system))
            {
                return system;
            }

            throw new InvalidOperationException($"SystemLocator: system {typeof(T).Name} is not registered.");
        }
    }
}
