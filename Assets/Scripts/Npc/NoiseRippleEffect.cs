using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// White expanding ripple rings (same style as click-to-move, white).
    /// </summary>
    public sealed class NoiseRippleEffect : MonoBehaviour
    {
        [SerializeField] int _ringCount = 3;
        [SerializeField] float _duration = 1f;
        [SerializeField] float _maxRadius = 4.5f;
        [SerializeField] float _startWidth = 0.1f;
        [SerializeField] Color _color = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] float _ringDelay = 0.14f;
        [SerializeField] int _segments = 48;

        LineRenderer[] _rings;
        float _elapsed;
        bool _playing;
        bool _autoDestroy = true;

        public static NoiseRippleEffect Play(Vector3 worldPosition, float maxRadius, float duration = 1f)
        {
            var go = new GameObject("NoiseRipple");
            go.transform.position = worldPosition;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var effect = go.AddComponent<NoiseRippleEffect>();
            effect._maxRadius = Mathf.Max(0.2f, maxRadius);
            effect._duration = Mathf.Max(0.15f, duration);
            effect.BuildRings();
            effect.Restart();
            return effect;
        }

        public void ConfigureLooping(float maxRadius, float duration)
        {
            _maxRadius = Mathf.Max(0.2f, maxRadius);
            _duration = Mathf.Max(0.15f, duration);
            _autoDestroy = false;
            if (_rings == null || _rings.Length == 0)
            {
                BuildRings();
            }
        }

        public void Restart()
        {
            _elapsed = 0f;
            _playing = true;
            if (_rings == null || _rings.Length == 0)
            {
                BuildRings();
            }

            for (var i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                if (ring == null)
                {
                    continue;
                }

                ring.enabled = true;
                ApplyRing(ring, 0.05f, 1f);
            }
        }

        void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            var allDone = true;
            var cycle = _duration + (_ringCount - 1) * _ringDelay;

            for (var i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                if (ring == null)
                {
                    continue;
                }

                var localT = (_elapsed - i * _ringDelay) / _duration;
                if (localT < 0f)
                {
                    ring.enabled = false;
                    allDone = false;
                    continue;
                }

                if (localT >= 1f)
                {
                    ring.enabled = false;
                    continue;
                }

                allDone = false;
                ring.enabled = true;
                var radius = Mathf.Lerp(0.05f, _maxRadius, localT);
                var alpha = 1f - localT;
                ApplyRing(ring, radius, alpha);
            }

            if (!allDone)
            {
                return;
            }

            if (_autoDestroy)
            {
                _playing = false;
                Destroy(gameObject);
                return;
            }

            // Continuous drag: loop the ripple.
            if (_elapsed >= cycle)
            {
                Restart();
            }
        }

        void BuildRings()
        {
            _rings = new LineRenderer[_ringCount];
            for (var i = 0; i < _ringCount; i++)
            {
                var child = new GameObject("Ring_" + i);
                child.transform.SetParent(transform, false);
                var lr = child.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = _segments;
                lr.widthMultiplier = 1f;
                lr.numCornerVertices = 2;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.material = CreateRippleMaterial();
                lr.startColor = _color;
                lr.endColor = _color;
                _rings[i] = lr;
            }
        }

        void ApplyRing(LineRenderer ring, float radius, float alpha)
        {
            var width = Mathf.Lerp(_startWidth, _startWidth * 0.25f, 1f - alpha);
            ring.startWidth = width;
            ring.endWidth = width;

            var c = _color;
            c.a = _color.a * alpha;
            ring.startColor = c;
            ring.endColor = c;

            for (var s = 0; s < _segments; s++)
            {
                var angle = (s / (float)_segments) * Mathf.PI * 2f;
                ring.SetPosition(s, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        static Material CreateRippleMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.color = Color.white;
            return mat;
        }
    }
}
