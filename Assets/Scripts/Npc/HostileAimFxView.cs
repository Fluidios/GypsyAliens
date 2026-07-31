using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Aim line + target reticle over the player; red circular aim progress above the hostile.
    /// Line lerps white→red; reticle grows with progress.
    /// </summary>
    public sealed class HostileAimFxView : MonoBehaviour
    {
        const int ProgressSegments = 48;

        [SerializeField] Transform _lineOrigin;
        [SerializeField] float _originHeight = 1.35f;
        [SerializeField] float _targetHeight = 1.05f;
        [SerializeField] float _reticleHeight = 2.05f;
        [SerializeField] float _progressHeight = 2.25f;
        [SerializeField] float _lineWidth = 0.04f;
        [SerializeField] float _progressRadius = 0.38f;
        [SerializeField] float _reticleScaleMin = 0.7f;
        [SerializeField] float _reticleScaleMax = 1.55f;
        [SerializeField] Color _lineColorStart = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] Color _lineColorEnd = new Color(1f, 0.12f, 0.1f, 0.95f);
        [SerializeField] Color _reticleColor = new Color(1f, 0.2f, 0.15f, 1f);
        [SerializeField] Color _progressColor = new Color(1f, 0.15f, 0.12f, 0.95f);
        [SerializeField] Color _progressBgColor = new Color(0.45f, 0.08f, 0.06f, 0.35f);

        LineRenderer _line;
        LineRenderer _progressBg;
        LineRenderer _progressFill;
        Transform _reticleRoot;
        Transform _progressRoot;
        TextMesh _reticleText;
        Material _lineMat;
        Material _progressMat;
        bool _visible;
        float _progress;
        Transform _currentTarget;

        public void SetLineOrigin(Transform origin)
        {
            _lineOrigin = origin;
        }

        void Awake()
        {
            EnsureFx();
            SetActive(false, null, 0f);
        }

        void OnDestroy()
        {
            if (_lineMat != null)
            {
                Destroy(_lineMat);
                _lineMat = null;
            }

            if (_progressMat != null)
            {
                Destroy(_progressMat);
                _progressMat = null;
            }

            if (_reticleRoot != null)
            {
                Destroy(_reticleRoot.gameObject);
                _reticleRoot = null;
            }
        }

        public void SetActive(bool active, Transform target, float progress01)
        {
            EnsureFx();
            _progress = Mathf.Clamp01(progress01);
            _currentTarget = target;
            _visible = active && target != null;

            if (_line != null)
            {
                _line.enabled = _visible;
            }

            if (_reticleRoot != null)
            {
                _reticleRoot.gameObject.SetActive(_visible);
            }

            if (_progressRoot != null)
            {
                _progressRoot.gameObject.SetActive(_visible);
            }

            if (_progressBg != null)
            {
                _progressBg.enabled = _visible;
            }

            if (_progressFill != null)
            {
                _progressFill.enabled = _visible;
            }

            if (_visible)
            {
                ApplyProgressVisuals();
                UpdatePositions(target);
            }
        }

        void LateUpdate()
        {
            if (!_visible)
            {
                return;
            }

            if (_currentTarget != null)
            {
                UpdatePositions(_currentTarget);
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            if (_reticleRoot != null && _reticleRoot.gameObject.activeSelf)
            {
                // Billboard toward camera, then roll 45° so "+" reads as "×".
                _reticleRoot.rotation = Quaternion.LookRotation(
                    _reticleRoot.position - cam.transform.position,
                    Vector3.up) * Quaternion.Euler(0f, 0f, 45f);
            }

            if (_progressRoot != null && _progressRoot.gameObject.activeSelf)
            {
                _progressRoot.rotation = Quaternion.LookRotation(
                    _progressRoot.position - cam.transform.position,
                    Vector3.up);
            }
        }

        public void UpdatePositions(Transform target)
        {
            if (target == null || _line == null)
            {
                return;
            }

            var origin = _lineOrigin != null
                ? _lineOrigin.position
                : transform.position + Vector3.up * _originHeight;
            var aimPoint = target.position + Vector3.up * _targetHeight;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, aimPoint);

            if (_reticleRoot != null)
            {
                _reticleRoot.position = target.position + Vector3.up * _reticleHeight;
            }

            if (_progressRoot != null)
            {
                _progressRoot.position = transform.position + Vector3.up * _progressHeight;
            }

            RebuildProgressRings();
        }

        void ApplyProgressVisuals()
        {
            var color = Color.Lerp(_lineColorStart, _lineColorEnd, _progress);
            if (_lineMat != null)
            {
                ApplyMatColor(_lineMat, color);
            }

            if (_line != null)
            {
                _line.startColor = color;
                _line.endColor = color;
                var w = _lineWidth * (0.85f + 0.35f * _progress);
                _line.startWidth = w;
                _line.endWidth = w * 0.65f;
            }

            if (_reticleRoot != null)
            {
                var scale = Mathf.Lerp(_reticleScaleMin, _reticleScaleMax, _progress);
                _reticleRoot.localScale = Vector3.one * scale;
            }

            if (_reticleText != null)
            {
                _reticleText.color = Color.Lerp(_lineColorStart, _reticleColor, Mathf.Max(0.25f, _progress));
            }

            RebuildProgressRings();
        }

        void RebuildProgressRings()
        {
            if (_progressBg == null || _progressFill == null || _progressRoot == null)
            {
                return;
            }

            var center = _progressRoot.position;
            var cam = Camera.main;
            var normal = cam != null ? (cam.transform.position - center).normalized : Vector3.forward;
            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = Vector3.forward;
            }

            var up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, normal)) > 0.95f)
            {
                up = Vector3.right;
            }

            var right = Vector3.Cross(up, normal).normalized;
            up = Vector3.Cross(normal, right).normalized;
            var radius = _progressRadius;

            for (var i = 0; i < ProgressSegments; i++)
            {
                var ang = i / (float)ProgressSegments * Mathf.PI * 2f;
                var p = center + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * radius;
                _progressBg.SetPosition(i, p);
            }

            var fillCount = Mathf.Max(2, Mathf.CeilToInt(_progress * (ProgressSegments - 1)) + 1);
            _progressFill.positionCount = fillCount;
            for (var i = 0; i < fillCount; i++)
            {
                var t = fillCount <= 1 ? 0f : i / (float)(fillCount - 1);
                var ang = t * _progress * Mathf.PI * 2f - Mathf.PI * 0.5f;
                var p = center + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * radius;
                _progressFill.SetPosition(i, p);
            }

            _progressFill.startColor = _progressColor;
            _progressFill.endColor = _progressColor;
            _progressBg.startColor = _progressBgColor;
            _progressBg.endColor = _progressBgColor;
        }

        void EnsureFx()
        {
            if (_line == null)
            {
                var lineGo = new GameObject("AimLine");
                lineGo.transform.SetParent(transform, false);
                _line = lineGo.AddComponent<LineRenderer>();
                _line.positionCount = 2;
                _line.useWorldSpace = true;
                _line.startWidth = _lineWidth;
                _line.endWidth = _lineWidth * 0.65f;
                _line.shadowCastingMode = ShadowCastingMode.Off;
                _line.receiveShadows = false;
                _line.allowOcclusionWhenDynamic = false;
                _line.numCapVertices = 2;

                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                _lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                ApplyMatColor(_lineMat, _lineColorStart);
                _lineMat.renderQueue = 3100;
                _line.sharedMaterial = _lineMat;
                _line.startColor = _lineColorStart;
                _line.endColor = _lineColorStart;
            }

            if (_reticleRoot == null)
            {
                var go = new GameObject("AimReticle");
                go.transform.SetParent(null, true);
                _reticleRoot = go.transform;

                _reticleText = go.AddComponent<TextMesh>();
                _reticleText.text = "+";
                _reticleText.anchor = TextAnchor.MiddleCenter;
                _reticleText.alignment = TextAlignment.Center;
                _reticleText.characterSize = 0.18f;
                _reticleText.fontSize = 64;
                _reticleText.fontStyle = FontStyle.Bold;
                _reticleText.color = _reticleColor;
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            if (_progressRoot == null)
            {
                var go = new GameObject("AimProgress");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * _progressHeight;
                _progressRoot = go.transform;

                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                _progressMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                ApplyMatColor(_progressMat, _progressColor);
                _progressMat.renderQueue = 3100;

                _progressBg = CreateRingLine("AimProgressBg", go.transform, _progressMat, true);
                _progressFill = CreateRingLine("AimProgressFill", go.transform, _progressMat, false);
                _progressFill.widthMultiplier = 0.06f;
                _progressBg.widthMultiplier = 0.032f;
            }
        }

        static LineRenderer CreateRingLine(string name, Transform parent, Material mat, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = ProgressSegments;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.numCapVertices = 2;
            return lr;
        }

        static void ApplyMatColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }
    }
}
