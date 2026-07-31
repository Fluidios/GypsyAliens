using Fusion;
using GypsyAliens.Core;
using GypsyAliens.Network;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Host-simulated thrown rock on a parabola from start to floor aim point.
    /// Stops on walls; stuns fearful NPCs on hit; plays impact VFX on end.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ThrownRock : NetworkBehaviour
    {
        public const float DefaultHitRadius = 0.55f;
        public const float DefaultWallRadius = 0.1f;
        public const float DefaultGroundHeight = 0.12f;

        [SerializeField] float _hitRadius = DefaultHitRadius;
        [SerializeField] float _wallRadius = DefaultWallRadius;
        [SerializeField] float _groundHeight = DefaultGroundHeight;
        [SerializeField] float _horizontalSpeed = 7f;
        [SerializeField] float _minDuration = 0.25f;
        [SerializeField] float _maxDuration = 1.8f;
        [SerializeField] float _arcHeightFactor = 0.28f;
        [SerializeField] float _minArcHeight = 0.4f;
        [SerializeField] float _maxArcHeight = 3.5f;
        [SerializeField] LayerMask _wallMask;
        [SerializeField] LayerMask _npcHitMask = ~0;

        Vector3 _start;
        Vector3 _end;
        float _duration;
        float _arcHeight;
        float _elapsed;
        bool _initialized;

        public void Init(Vector3 start, Vector3 end)
        {
            _start = start;
            _end = end;
            _end.y = _groundHeight;

            var flat = _end - _start;
            flat.y = 0f;
            var distance = Mathf.Max(0.15f, flat.magnitude);
            _duration = Mathf.Clamp(distance / Mathf.Max(0.1f, _horizontalSpeed), _minDuration, _maxDuration);
            _arcHeight = ComputeArcHeight(distance, _arcHeightFactor, _minArcHeight, _maxArcHeight);
            _elapsed = 0f;
            _initialized = true;
            transform.position = _start;
        }

        public override void Spawned()
        {
            if (_wallMask.value == 0)
            {
                _wallMask = GameLayers.WallMask;
            }

            EnsureRockMaterial();
        }

        void EnsureRockMaterial()
        {
            var renderer = GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                return;
            }

            var mat = new Material(shader)
            {
                name = "ThrownRock_Runtime",
                hideFlags = HideFlags.HideAndDontSave,
            };
            var color = new Color(0.45f, 0.42f, 0.4f, 1f);
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            mat.renderQueue = 3000;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public static float ComputeArcHeight(float distance, float factor = 0.28f, float minHeight = 0.4f, float maxHeight = 3.5f)
        {
            return Mathf.Clamp(distance * factor, minHeight, maxHeight);
        }

        public static Vector3 EvaluateParabola(Vector3 start, Vector3 end, float arcHeight, float t)
        {
            t = Mathf.Clamp01(t);
            var pos = Vector3.Lerp(start, end, t);
            pos.y += arcHeight * 4f * t * (1f - t);
            return pos;
        }

        /// <summary>
        /// True if the throw parabola is blocked by a wall before the landing point.
        /// Ignores the first stretch near the thrower to avoid false positives.
        /// </summary>
        public static bool IsTrajectoryBlocked(
            Vector3 start,
            Vector3 end,
            float arcHeight,
            float radius,
            LayerMask wallMask,
            int segments = 24,
            float ignoreDistance = 0.55f)
        {
            segments = Mathf.Max(2, segments);
            var prev = start;
            var traveled = 0f;
            for (var i = 1; i <= segments; i++)
            {
                var next = EvaluateParabola(start, end, arcHeight, i / (float)segments);
                var delta = next - prev;
                var dist = delta.magnitude;
                if (dist > 0.0001f)
                {
                    traveled += dist;
                    if (traveled >= ignoreDistance)
                    {
                        var dir = delta / dist;
                        if (Physics.SphereCast(prev, radius, dir, out _, dist, wallMask, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }
                    }
                }

                prev = next;
            }

            return false;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !_initialized)
            {
                return;
            }

            var prev = transform.position;
            _elapsed += Runner.DeltaTime;
            var t = _elapsed / _duration;
            var next = EvaluateParabola(_start, _end, _arcHeight, t);

            var delta = next - prev;
            var dist = delta.magnitude;
            if (dist > 0.0001f)
            {
                var dir = delta / dist;
                if (Physics.SphereCast(prev, _wallRadius, dir, out var hit, dist, _wallMask, QueryTriggerInteraction.Ignore))
                {
                    Finish(hit.point);
                    return;
                }
            }

            if (TryHitNpc(next, out var impact))
            {
                Finish(impact);
                return;
            }

            transform.position = next;

            if (t >= 1f)
            {
                Finish(_end);
            }
        }

        bool TryHitNpc(Vector3 rockPosition, out Vector3 impactPoint)
        {
            impactPoint = rockPosition;
            var thrower = Object != null ? Object.InputAuthority : PlayerRef.None;

            // Physics query (hit capsule on NPCs / players).
            var hits = Physics.OverlapSphere(rockPosition, _hitRadius, _npcHitMask, QueryTriggerInteraction.Collide);
            for (var i = 0; i < hits.Length; i++)
            {
                var fearful = hits[i].GetComponentInParent<NetworkFearfulNpc>();
                if (fearful != null
                    && fearful.Object != null
                    && fearful.Object.IsValid
                    && !fearful.IsExtracting)
                {
                    fearful.ApplyStun();
                    impactPoint = fearful.transform.position;
                    return true;
                }

                var hostile = hits[i].GetComponentInParent<NetworkHostileNpc>();
                if (hostile != null
                    && hostile.Object != null
                    && hostile.Object.IsValid
                    && !hostile.IsStunned)
                {
                    hostile.ApplyStun(_start);
                    impactPoint = hostile.transform.position;
                    return true;
                }

                var player = hits[i].GetComponentInParent<NetworkPlayerController>();
                if (player != null
                    && player.Object != null
                    && player.Object.IsValid
                    && !player.IsStunned
                    && player.Object.InputAuthority != thrower)
                {
                    player.ApplyStun();
                    impactPoint = player.transform.position + Vector3.up * 0.9f;
                    return true;
                }
            }

            // Fallback proximity — works even if collider setup lags a frame after spawn.
            var animals = FindObjectsByType<NetworkFearfulNpc>(FindObjectsSortMode.None);
            var hitRange = _hitRadius + 0.35f;
            var hitRangeSq = hitRange * hitRange;
            for (var i = 0; i < animals.Length; i++)
            {
                var npc = animals[i];
                if (npc == null || !npc.Object || !npc.Object.IsValid || npc.IsExtracting)
                {
                    continue;
                }

                var body = npc.transform.position + Vector3.up * 0.3f;
                var offset = body - rockPosition;
                if (offset.sqrMagnitude > hitRangeSq)
                {
                    continue;
                }

                npc.ApplyStun();
                impactPoint = body;
                return true;
            }

            var hostiles = FindObjectsByType<NetworkHostileNpc>(FindObjectsSortMode.None);
            var hostileRange = _hitRadius + 0.45f;
            var hostileRangeSq = hostileRange * hostileRange;
            for (var i = 0; i < hostiles.Length; i++)
            {
                var npc = hostiles[i];
                if (npc == null || !npc.Object || !npc.Object.IsValid || npc.IsStunned)
                {
                    continue;
                }

                var body = npc.transform.position + Vector3.up * 0.9f;
                if ((body - rockPosition).sqrMagnitude > hostileRangeSq)
                {
                    continue;
                }

                npc.ApplyStun(_start);
                impactPoint = body;
                return true;
            }

            var players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            var playerRange = _hitRadius + 0.5f;
            var playerRangeSq = playerRange * playerRange;
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null
                    || player.Object == null
                    || !player.Object.IsValid
                    || player.IsStunned
                    || player.Object.InputAuthority == thrower)
                {
                    continue;
                }

                var body = player.transform.position + Vector3.up * 0.9f;
                if ((body - rockPosition).sqrMagnitude > playerRangeSq)
                {
                    continue;
                }

                player.ApplyStun();
                impactPoint = body;
                return true;
            }

            return false;
        }

        void Finish(Vector3 impactPoint)
        {
            RockImpactVfx.Play(impactPoint);
            RPC_PlayImpact(impactPoint);
            NoiseRegistry.EmitPulse(impactPoint, radius: 4.5f, duration: NoisePulse.DefaultDuration, loudness: 1f);
            if (Runner != null && Object != null && Object.IsValid)
            {
                Runner.Despawn(Object);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_PlayImpact(Vector3 impactPoint)
        {
            RockImpactVfx.Play(impactPoint);
            NoiseRegistry.EmitPulse(impactPoint, radius: 4.5f, duration: NoisePulse.DefaultDuration, loudness: 1f);
        }
    }
}
