using GypsyAliens.Core;
using GypsyAliens.Npc;
using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Network
{
    /// <summary>
    /// Local parabolic aim preview + animated floor reticle for rock throw.
    /// Red when blocked by a wall. Does not write depth.
    /// </summary>
    public sealed class PlayerThrowAimView : MonoBehaviour
    {
        [SerializeField] LineRenderer _line;
        [SerializeField] Color _clearColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        [SerializeField] Color _blockedColor = new Color(1f, 0.2f, 0.15f, 0.95f);
        [SerializeField] float _width = 0.07f;
        [SerializeField] int _segments = 24;
        [SerializeField] float _arcHeightFactor = 0.28f;
        [SerializeField] float _minArcHeight = 0.4f;
        [SerializeField] float _maxArcHeight = 3.5f;
        [SerializeField] float _throwHeight = 0.6f;
        [SerializeField] float _groundHeight = ThrownRock.DefaultGroundHeight;
        [SerializeField] float _blockCheckRadius = ThrownRock.DefaultWallRadius;
        [SerializeField] LayerMask _wallMask;
        [SerializeField] float _reticleBaseRadius = 0.35f;
        [SerializeField] float _reticlePulse = 0.08f;
        [SerializeField] float _reticleSpinSpeed = 90f;
        [SerializeField] int _reticleSegments = 40;

        Material _lineMaterial;
        Material _reticleMaterial;
        LineRenderer _reticleRing;
        LineRenderer _reticleCrossA;
        LineRenderer _reticleCrossB;
        Transform _reticleRoot;
        bool _lastBlocked;
        bool _visible;
        Color _currentColor;

        void Awake()
        {
            if (_wallMask.value == 0)
            {
                _wallMask = GameLayers.WallMask;
            }

            // Keep aim visuals on a world root so player teleport / launch / disable
            // never hides or warps the preview with the character transform.
            var rootGo = new GameObject("PlayerThrowAimWorld");
            var worldRoot = rootGo.transform;

            // Never reparent the player (prefab may already have a LineRenderer on this GO).
            var existingOnPlayer = GetComponent<LineRenderer>();
            if (existingOnPlayer != null)
            {
                existingOnPlayer.enabled = false;
                if (_line == existingOnPlayer)
                {
                    _line = null;
                }
            }

            _line = rootGo.AddComponent<LineRenderer>();
            _line.positionCount = _segments;
            _line.startWidth = _width;
            _line.endWidth = _width * 0.45f;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.allowOcclusionWhenDynamic = false;

            _lineMaterial = CreateTransparentMaterial(_clearColor);
            _line.sharedMaterial = _lineMaterial;
            _line.enabled = false;

            BuildReticle(worldRoot);
            _currentColor = _clearColor;
            ApplyColors(_clearColor);
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (_line != null && _line.gameObject != null && _line.gameObject != gameObject)
            {
                Destroy(_line.gameObject);
            }
        }

        void BuildReticle(Transform parent)
        {
            var rootGo = new GameObject("ThrowFloorReticle");
            rootGo.transform.SetParent(parent, false);
            _reticleRoot = rootGo.transform;

            _reticleMaterial = CreateTransparentMaterial(_clearColor);

            _reticleRing = CreateReticleLine("Ring", _reticleSegments, loop: true);
            _reticleCrossA = CreateReticleLine("CrossA", 2, loop: false);
            _reticleCrossB = CreateReticleLine("CrossB", 2, loop: false);
        }

        LineRenderer CreateReticleLine(string name, int count, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_reticleRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = count;
            lr.loop = loop;
            lr.useWorldSpace = false;
            lr.startWidth = 0.045f;
            lr.endWidth = 0.045f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            lr.sharedMaterial = _reticleMaterial;
            lr.enabled = false;
            return lr;
        }

        static Material CreateTransparentMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_line != null)
            {
                _line.enabled = visible;
            }

            if (_reticleRing != null)
            {
                _reticleRing.enabled = visible;
            }

            if (_reticleCrossA != null)
            {
                _reticleCrossA.enabled = visible;
            }

            if (_reticleCrossB != null)
            {
                _reticleCrossB.enabled = visible;
            }

            if (_reticleRoot != null)
            {
                _reticleRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Updates the parabola + floor reticle. Returns false when a wall blocks the throw.
        /// </summary>
        public bool UpdateAim(Vector3 playerPosition, Vector3 cursorFloorPoint)
        {
            if (_line == null)
            {
                return false;
            }

            if (_wallMask.value == 0)
            {
                _wallMask = GameLayers.WallMask;
            }

            var flatDir = cursorFloorPoint - playerPosition;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.01f)
            {
                flatDir = transform.forward;
            }

            flatDir.Normalize();
            var start = playerPosition + Vector3.up * _throwHeight + flatDir * 0.35f;
            var end = cursorFloorPoint;
            end.y = _groundHeight;

            var flat = end - start;
            flat.y = 0f;
            var distance = flat.magnitude;
            var arcHeight = ThrownRock.ComputeArcHeight(distance, _arcHeightFactor, _minArcHeight, _maxArcHeight);
            var blocked = ThrownRock.IsTrajectoryBlocked(
                start, end, arcHeight, _blockCheckRadius, _wallMask, _segments);

            SetVisible(true);
            _line.positionCount = _segments;
            for (var i = 0; i < _segments; i++)
            {
                var t = i / (float)(_segments - 1);
                _line.SetPosition(i, ThrownRock.EvaluateParabola(start, end, arcHeight, t));
            }

            UpdateReticle(end);

            if (blocked != _lastBlocked)
            {
                _lastBlocked = blocked;
                ApplyColors(blocked ? _blockedColor : _clearColor);
            }

            return !blocked;
        }

        void Update()
        {
            if (!_visible || _reticleRoot == null)
            {
                return;
            }

            var pulse = 1f + Mathf.Sin(Time.time * 6f) * (_reticlePulse / Mathf.Max(0.01f, _reticleBaseRadius));
            var radius = _reticleBaseRadius * pulse;
            _reticleRoot.Rotate(0f, _reticleSpinSpeed * Time.deltaTime, 0f, Space.World);

            if (_reticleRing != null)
            {
                for (var i = 0; i < _reticleSegments; i++)
                {
                    var angle = (i / (float)_reticleSegments) * Mathf.PI * 2f;
                    _reticleRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.03f, Mathf.Sin(angle) * radius));
                }
            }

            var cross = radius * 0.55f;
            if (_reticleCrossA != null)
            {
                _reticleCrossA.SetPosition(0, new Vector3(-cross, 0.03f, 0f));
                _reticleCrossA.SetPosition(1, new Vector3(cross, 0.03f, 0f));
            }

            if (_reticleCrossB != null)
            {
                _reticleCrossB.SetPosition(0, new Vector3(0f, 0.03f, -cross));
                _reticleCrossB.SetPosition(1, new Vector3(0f, 0.03f, cross));
            }

            // Soft alpha pulse on reticle materials.
            var alphaPulse = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f));
            var c = _currentColor;
            c.a = _currentColor.a * alphaPulse;
            ApplyMaterialColor(_reticleMaterial, c);
            if (_reticleRing != null)
            {
                _reticleRing.startColor = c;
                _reticleRing.endColor = c;
            }

            if (_reticleCrossA != null)
            {
                _reticleCrossA.startColor = c;
                _reticleCrossA.endColor = c;
            }

            if (_reticleCrossB != null)
            {
                _reticleCrossB.startColor = c;
                _reticleCrossB.endColor = c;
            }
        }

        void UpdateReticle(Vector3 floorPoint)
        {
            if (_reticleRoot == null)
            {
                return;
            }

            _reticleRoot.position = floorPoint;
            if (!_reticleRoot.gameObject.activeSelf)
            {
                _reticleRoot.gameObject.SetActive(true);
            }
        }

        void ApplyColors(Color color)
        {
            _currentColor = color;
            ApplyMaterialColor(_lineMaterial, color);
            ApplyMaterialColor(_reticleMaterial, color);

            if (_line != null)
            {
                _line.startColor = color;
                _line.endColor = color;
            }
        }

        static void ApplyMaterialColor(Material mat, Color color)
        {
            if (mat == null)
            {
                return;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }
        }
    }
}
