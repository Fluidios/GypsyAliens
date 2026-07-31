using System.Collections.Generic;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Runtime registry of audible noise sources (rock pulses, drag shuffling).
    /// </summary>
    public static class NoiseRegistry
    {
        static readonly List<NoiseSource> Sources = new List<NoiseSource>(16);

        public static void Register(NoiseSource source)
        {
            if (source != null && !Sources.Contains(source))
            {
                Sources.Add(source);
            }
        }

        public static void Unregister(NoiseSource source)
        {
            Sources.Remove(source);
        }

        /// <summary>
        /// Returns true if any active noise radius covers <paramref name="listenerWorld"/>.
        /// Picks the nearest source center.
        /// </summary>
        public static bool TryGetAudible(Vector3 listenerWorld, out Vector3 sourcePosition, out float radius)
        {
            sourcePosition = listenerWorld;
            radius = 0f;
            var best = float.MaxValue;
            var found = false;

            for (var i = Sources.Count - 1; i >= 0; i--)
            {
                var src = Sources[i];
                if (src == null)
                {
                    Sources.RemoveAt(i);
                    continue;
                }

                if (!src.IsAudible)
                {
                    continue;
                }

                var pos = src.WorldPosition;
                var flat = pos - listenerWorld;
                flat.y = 0f;
                var distSq = flat.sqrMagnitude;
                var r = src.Radius;
                if (distSq > r * r)
                {
                    continue;
                }

                if (distSq < best)
                {
                    best = distSq;
                    sourcePosition = pos;
                    radius = r;
                    found = true;
                }
            }

            return found;
        }

        public static NoisePulse EmitPulse(Vector3 worldPosition, float radius, float duration, float loudness = 1f)
        {
            var go = new GameObject("NoisePulse");
            go.transform.position = worldPosition;
            var pulse = go.AddComponent<NoisePulse>();
            pulse.Configure(radius, duration, loudness);
            return pulse;
        }
    }
}
