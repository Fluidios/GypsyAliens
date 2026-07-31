using System.Collections.Generic;
using GypsyAliens.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Level
{
    /// <summary>
    /// Fades walls and door frames near the cursor to semi-transparent while keeping opaque shadow casting.
    /// </summary>
    public sealed class WallXRaySystem : GameSystemBehaviour<WallXRaySystem>
    {
        const float CharacterHeight = 1.8f;

        [SerializeField] float _rayLength = 500f;
        [SerializeField] [Range(0.05f, 1f)] float _fadeAlpha = 0.35f;
        [Tooltip("World-space radius around the cursor. Default is one character height.")]
        [SerializeField] float _fadeRadius = CharacterHeight;
        [SerializeField] float _behindProbe = 0.2f;
        [SerializeField] Material _sourcePrototypeMaterial;

        readonly List<FadedWall> _faded = new List<FadedWall>();
        readonly Dictionary<Renderer, ShadowProxy> _proxies = new Dictionary<Renderer, ShadowProxy>();
        readonly HashSet<Renderer> _fadeSet = new HashSet<Renderer>();
        readonly HashSet<Collider> _wallCols = new HashSet<Collider>();
        readonly RaycastHit[] _hits = new RaycastHit[32];
        readonly Collider[] _overlap = new Collider[96];

        Material _fadeMaterial;

        struct FadedWall
        {
            public Renderer Renderer;
            public Material[] OriginalShared;
            public ShadowCastingMode OriginalShadowMode;
        }

        struct ShadowProxy
        {
            public MeshRenderer Renderer;
        }

        protected override void Awake()
        {
            EnsureFadeMaterial();
            base.Awake();
        }

        void EnsureFadeMaterial()
        {
            if (_fadeMaterial != null)
            {
                return;
            }

            var shader = Shader.Find("GypsyAliens/XRayFade");
            if (shader == null)
            {
                Debug.LogError("WallXRaySystem: GypsyAliens/XRayFade shader not found.", this);
                return;
            }

            _fadeMaterial = new Material(shader)
            {
                name = "WallXRay_Fade",
                hideFlags = HideFlags.HideAndDontSave,
            };

            ApplyPrototypeLook(_fadeMaterial);
        }

        void ApplyPrototypeLook(Material fade)
        {
            var source = _sourcePrototypeMaterial;
#if UNITY_EDITOR
            if (source == null)
            {
                source = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Synty/PolygonPrototype/Materials/PolygonPrototype_Grid_01.mat");
            }
#endif
            Color baseColor = new Color(1f, 0.724f, 0f, _fadeAlpha);
            Texture grid = null;
            var gridScale = 1f;
            var falloff = 50f;
            var overlay = 0.5f;

            if (source != null)
            {
                if (source.HasProperty("_BaseColor"))
                {
                    baseColor = source.GetColor("_BaseColor");
                }
                else if (source.HasProperty("_Base_Color"))
                {
                    baseColor = source.GetColor("_Base_Color");
                }

                if (source.HasProperty("_Grid"))
                {
                    grid = source.GetTexture("_Grid");
                }

                if (source.HasProperty("_GridScale"))
                {
                    gridScale = source.GetFloat("_GridScale");
                }
                else if (source.HasProperty("_Grid_Scale"))
                {
                    gridScale = source.GetFloat("_Grid_Scale");
                }

                if (source.HasProperty("_Falloff"))
                {
                    falloff = source.GetFloat("_Falloff");
                }

                if (source.HasProperty("_OverlayAmount"))
                {
                    overlay = source.GetFloat("_OverlayAmount");
                }
                else if (source.HasProperty("_Overlay_Amount"))
                {
                    overlay = source.GetFloat("_Overlay_Amount");
                }
            }

            baseColor.a = _fadeAlpha;
            fade.SetColor("_BaseColor", baseColor);
            fade.SetFloat("_GridScale", gridScale);
            fade.SetFloat("_Falloff", falloff);
            fade.SetFloat("_OverlayAmount", overlay);
            if (grid != null)
            {
                fade.SetTexture("_Grid", grid);
            }
        }

        void LateUpdate()
        {
            ClearFade();

            var cam = UnityEngine.Camera.main;
            if (cam == null || MouseUnavailable() || _fadeMaterial == null)
            {
                return;
            }

            // Keep alpha editable live in the inspector.
            var c = _fadeMaterial.GetColor("_BaseColor");
            c.a = _fadeAlpha;
            _fadeMaterial.SetColor("_BaseColor", c);

            var wallMask = GameLayers.WallMask;
            var floorMask = GameLayers.FloorMask;
            if (wallMask == 0)
            {
                return;
            }

            if (!TryGetCursorPoint(cam, wallMask | floorMask, out var cursorPoint))
            {
                return;
            }

            var radius = Mathf.Max(0.1f, _fadeRadius);
            // Include doorway trigger colliders.
            var hitCount = Physics.OverlapSphereNonAlloc(
                cursorPoint,
                radius,
                _overlap,
                wallMask,
                QueryTriggerInteraction.Collide);

            if (hitCount <= 0)
            {
                return;
            }

            var camPos = cam.transform.position;
            _fadeSet.Clear();
            _wallCols.Clear();

            for (var i = 0; i < hitCount; i++)
            {
                var col = _overlap[i];
                if (col == null || !_wallCols.Add(col))
                {
                    continue;
                }

                if (!HasFloorBehindWall(camPos, col, floorMask))
                {
                    continue;
                }

                var renderers = col.GetComponentsInChildren<Renderer>();
                for (var r = 0; r < renderers.Length; r++)
                {
                    var renderer = renderers[r];
                    if (renderer != null && !IsShadowProxyRenderer(renderer))
                    {
                        _fadeSet.Add(renderer);
                    }
                }
            }

            foreach (var renderer in _fadeSet)
            {
                FadeRenderer(renderer);
            }
        }

        bool HasFloorBehindWall(Vector3 camPos, Collider wall, LayerMask floorMask)
        {
            if (wall == null)
            {
                return false;
            }

            Vector3 nearPoint;
            if (SupportsClosestPoint(wall))
            {
                nearPoint = wall.ClosestPoint(camPos);
            }
            else
            {
                // Non-convex MeshColliders reject ClosestPoint — use bounds instead.
                nearPoint = wall.bounds.ClosestPoint(camPos);
            }

            var away = nearPoint - camPos;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = wall.bounds.center - camPos;
            }

            var dir = away.normalized;
            // Start just past the near face so we look for room floor beyond the wall.
            var origin = nearPoint + dir * _behindProbe;
            return Physics.Raycast(
                origin,
                dir,
                out _,
                _rayLength,
                floorMask,
                QueryTriggerInteraction.Ignore);
        }

        static bool SupportsClosestPoint(Collider col)
        {
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
            {
                return true;
            }

            return col is MeshCollider mesh && mesh.convex;
        }

        static bool IsShadowProxyRenderer(Renderer renderer)
        {
            return renderer != null && renderer.gameObject.name == "WallXRay_ShadowProxy";
        }

        bool TryGetCursorPoint(UnityEngine.Camera cam, LayerMask mask, out Vector3 point)
        {
            point = default;
            var ray = cam.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
            var count = Physics.RaycastNonAlloc(ray, _hits, _rayLength, mask, QueryTriggerInteraction.Ignore);
            if (count <= 0)
            {
                return false;
            }

            var best = 0;
            for (var i = 1; i < count; i++)
            {
                if (_hits[i].distance < _hits[best].distance)
                {
                    best = i;
                }
            }

            point = _hits[best].point;
            return true;
        }

        static bool MouseUnavailable()
        {
            return UnityEngine.InputSystem.Mouse.current == null;
        }

        void FadeRenderer(Renderer renderer)
        {
            if (IsShadowProxyRenderer(renderer))
            {
                return;
            }

            EnsureFadeMaterial();
            EnsureShadowProxy(renderer);

            var shared = renderer.sharedMaterials;
            var overrides = new Material[shared.Length];
            for (var i = 0; i < overrides.Length; i++)
            {
                overrides[i] = _fadeMaterial;
            }

            var originalShadowMode = renderer.shadowCastingMode;
            renderer.sharedMaterials = overrides;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            if (_proxies.TryGetValue(renderer, out var proxy) && proxy.Renderer != null)
            {
                proxy.Renderer.sharedMaterials = shared;
                proxy.Renderer.enabled = true;
            }

            _faded.Add(new FadedWall
            {
                Renderer = renderer,
                OriginalShared = shared,
                OriginalShadowMode = originalShadowMode,
            });
        }

        void EnsureShadowProxy(Renderer source)
        {
            if (_proxies.TryGetValue(source, out var existing) && existing.Renderer != null)
            {
                return;
            }

            var meshFilter = source.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return;
            }

            var go = new GameObject("WallXRay_ShadowProxy")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            go.transform.SetParent(source.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = source.gameObject.layer;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = meshFilter.sharedMesh;

            var proxyRenderer = go.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = source.sharedMaterials;
            proxyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.lightProbeUsage = LightProbeUsage.Off;
            proxyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            proxyRenderer.enabled = false;

            _proxies[source] = new ShadowProxy
            {
                Renderer = proxyRenderer,
            };
        }

        void ClearFade()
        {
            for (var i = 0; i < _faded.Count; i++)
            {
                var entry = _faded[i];
                if (entry.Renderer != null)
                {
                    entry.Renderer.sharedMaterials = entry.OriginalShared;
                    entry.Renderer.shadowCastingMode = entry.OriginalShadowMode;

                    if (_proxies.TryGetValue(entry.Renderer, out var proxy) && proxy.Renderer != null)
                    {
                        proxy.Renderer.enabled = false;
                    }
                }
            }

            _faded.Clear();
        }

        void CleanupProxies()
        {
            foreach (var pair in _proxies)
            {
                if (pair.Value.Renderer != null)
                {
                    Destroy(pair.Value.Renderer.gameObject);
                }
            }

            _proxies.Clear();
        }

        protected override void OnDestroy()
        {
            ClearFade();
            CleanupProxies();
            if (_fadeMaterial != null)
            {
                Destroy(_fadeMaterial);
                _fadeMaterial = null;
            }

            base.OnDestroy();
        }
    }
}
