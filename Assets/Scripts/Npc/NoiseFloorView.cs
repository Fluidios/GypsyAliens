using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// White concentric floor rings showing noise radius / loudness.
    /// </summary>
    public sealed class NoiseFloorView : MonoBehaviour
    {
        const int Segments = 48;
        const int RingCount = 3;

        [SerializeField] float _floorOffset = 0.04f;
        [SerializeField] Color _color = new Color(1f, 1f, 1f, 0.55f);

        LineRenderer[] _rings;
        Material _mat;
        float _radius = 1f;
        float _loudness = 1f;
        bool _visible;

        public void Configure(float radius, float loudness)
        {
            _radius = Mathf.Max(0.05f, radius);
            _loudness = Mathf.Clamp01(loudness);
            EnsureRings();
            Rebuild();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            EnsureRings();
            for (var i = 0; i < _rings.Length; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].enabled = visible;
                }
            }
        }

        void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
        }

        void LateUpdate()
        {
            if (!_visible || _rings == null)
            {
                return;
            }

            var pos = transform.position;
            pos.y = _floorOffset;
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
            for (var i = 0; i < _rings.Length; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].transform.position = new Vector3(transform.position.x, _floorOffset, transform.position.z);
                }
            }
        }

        void EnsureRings()
        {
            if (_rings != null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (_mat.HasProperty("_BaseColor"))
            {
                _mat.SetColor("_BaseColor", _color);
            }
            else if (_mat.HasProperty("_Color"))
            {
                _mat.SetColor("_Color", _color);
            }

            _mat.renderQueue = 3000;

            _rings = new LineRenderer[RingCount];
            for (var i = 0; i < RingCount; i++)
            {
                var go = new GameObject("NoiseRing_" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.loop = true;
                lr.useWorldSpace = true;
                lr.positionCount = Segments;
                lr.widthMultiplier = 0.035f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.allowOcclusionWhenDynamic = false;
                lr.sharedMaterial = _mat;
                lr.startColor = _color;
                lr.endColor = _color;
                _rings[i] = lr;
            }
        }

        void Rebuild()
        {
            EnsureRings();
            var origin = transform.position;
            origin.y = _floorOffset;

            for (var r = 0; r < RingCount; r++)
            {
                var lr = _rings[r];
                var frac = (r + 1) / (float)RingCount;
                var ringRadius = _radius * frac;
                var alpha = _color.a * _loudness * (0.35f + 0.65f * frac);
                var col = new Color(_color.r, _color.g, _color.b, alpha);
                lr.startColor = col;
                lr.endColor = col;
                lr.widthMultiplier = 0.028f + 0.02f * _loudness;

                for (var i = 0; i < Segments; i++)
                {
                    var ang = i / (float)Segments * Mathf.PI * 2f;
                    var p = origin + new Vector3(Mathf.Cos(ang) * ringRadius, 0f, Mathf.Sin(ang) * ringRadius);
                    lr.SetPosition(i, p);
                }
            }
        }
    }
}
