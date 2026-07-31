using System.Collections.Generic;
using UnityEngine;

namespace GypsyAliens.Rendering
{
    /// <summary>
    /// Draws a flat-colored silhouette for mesh parts occluded by closer geometry (walls, door frames).
    /// Uses a stencil mask pass + fill pass as two materials so URP Deferred still draws them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OcclusionSilhouette : MonoBehaviour
    {
        static readonly Color DefaultPlayerColor = new Color(0.15f, 0.95f, 0.35f, 0.92f);
        static readonly Color DefaultNpcColor = new Color(0.95f, 0.2f, 0.2f, 0.92f);

        [SerializeField] Color _color = DefaultPlayerColor;
        [SerializeField] bool _includeInactiveChildren;

        Material _maskMaterial;
        Material _fillMaterial;
        readonly List<RendererEntry> _entries = new List<RendererEntry>(4);

        struct RendererEntry
        {
            public Renderer Renderer;
            public Material[] OriginalShared;
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                ApplyColor();
            }
        }

        public static Color PlayerColor => DefaultPlayerColor;
        public static Color NpcColor => DefaultNpcColor;

        void OnEnable()
        {
            EnsureMaterials();
            ApplyToRenderers();
        }

        void OnDisable()
        {
            RestoreRenderers();
        }

        void OnDestroy()
        {
            RestoreRenderers();
            DestroyMaterial(ref _maskMaterial);
            DestroyMaterial(ref _fillMaterial);
        }

        void OnValidate()
        {
            if (_fillMaterial != null)
            {
                ApplyColor();
            }
        }

        void EnsureMaterials()
        {
            if (_maskMaterial == null)
            {
                var maskShader = Shader.Find("GypsyAliens/OcclusionSilhouetteMask");
                if (maskShader == null)
                {
                    Debug.LogError("OcclusionSilhouette: GypsyAliens/OcclusionSilhouetteMask shader not found.", this);
                }
                else
                {
                    _maskMaterial = new Material(maskShader)
                    {
                        name = "OcclusionSilhouetteMask (Instance)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }

            if (_fillMaterial == null)
            {
                var fillShader = Shader.Find("GypsyAliens/OcclusionSilhouetteFill");
                if (fillShader == null)
                {
                    Debug.LogError("OcclusionSilhouette: GypsyAliens/OcclusionSilhouetteFill shader not found.", this);
                }
                else
                {
                    _fillMaterial = new Material(fillShader)
                    {
                        name = "OcclusionSilhouetteFill (Instance)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }

            ApplyColor();
        }

        void ApplyColor()
        {
            if (_fillMaterial != null)
            {
                _fillMaterial.SetColor("_Color", _color);
            }
        }

        void ApplyToRenderers()
        {
            RestoreRenderers();
            EnsureMaterials();
            if (_maskMaterial == null || _fillMaterial == null)
            {
                return;
            }

            var renderers = GetComponentsInChildren<Renderer>(_includeInactiveChildren);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsIgnoredRenderer(renderer))
                {
                    continue;
                }

                var original = renderer.sharedMaterials;
                var withSilhouette = new Material[original.Length + 2];
                for (var m = 0; m < original.Length; m++)
                {
                    withSilhouette[m] = original[m];
                }

                withSilhouette[original.Length] = _maskMaterial;
                withSilhouette[original.Length + 1] = _fillMaterial;
                renderer.sharedMaterials = withSilhouette;

                _entries.Add(new RendererEntry
                {
                    Renderer = renderer,
                    OriginalShared = original,
                });
            }
        }

        static bool IsIgnoredRenderer(Renderer renderer)
        {
            if (renderer.gameObject.name == "WallXRay_ShadowProxy")
            {
                return true;
            }

            return renderer is ParticleSystemRenderer;
        }

        void RestoreRenderers()
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Renderer != null)
                {
                    entry.Renderer.sharedMaterials = entry.OriginalShared;
                }
            }

            _entries.Clear();
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            Destroy(material);
            material = null;
        }
    }
}
