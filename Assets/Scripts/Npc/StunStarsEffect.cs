using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Orbiting stun stars. Depth write is off so they never trigger OcclusionSilhouette.
    /// </summary>
    public sealed class StunStarsEffect : MonoBehaviour
    {
        [SerializeField] int _starCount = 5;
        [SerializeField] float _radius = 0.35f;
        [SerializeField] float _height = 0.85f;
        [SerializeField] float _spinSpeed = 220f;
        [SerializeField] float _bobSpeed = 4f;
        [SerializeField] float _starScale = 0.12f;
        [SerializeField] Color _color = new Color(1f, 0.92f, 0.2f, 1f);

        Transform[] _stars;
        bool _active;
        Material _mat;

        void Awake()
        {
            EnsureStars();
            SetActive(false);
        }

        void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
        }

        void EnsureStars()
        {
            if (_stars != null)
            {
                return;
            }

            _stars = new Transform[_starCount];
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

            if (_mat.HasProperty("_ZWrite"))
            {
                _mat.SetFloat("_ZWrite", 0f);
            }

            if (_mat.HasProperty("_Surface"))
            {
                _mat.SetFloat("_Surface", 1f);
            }

            _mat.SetOverrideTag("RenderType", "Transparent");
            _mat.renderQueue = (int)RenderQueue.Transparent;
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            for (var i = 0; i < _starCount; i++)
            {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.name = "StunStar";
                star.transform.SetParent(transform, false);
                star.transform.localScale = Vector3.one * _starScale;
                Destroy(star.GetComponent<Collider>());
                var r = star.GetComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.allowOcclusionWhenDynamic = false;
                _stars[i] = star.transform;
            }
        }

        public void SetActive(bool active)
        {
            _active = active;
            EnsureStars();
            for (var i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] != null)
                {
                    _stars[i].gameObject.SetActive(active);
                }
            }
        }

        void LateUpdate()
        {
            if (!_active || _stars == null)
            {
                return;
            }

            var t = Time.time;
            for (var i = 0; i < _stars.Length; i++)
            {
                var angle = t * _spinSpeed + i * (360f / _stars.Length);
                var rad = angle * Mathf.Deg2Rad;
                var bob = Mathf.Sin(t * _bobSpeed + i) * 0.08f;
                _stars[i].localPosition = new Vector3(
                    Mathf.Cos(rad) * _radius,
                    _height + bob,
                    Mathf.Sin(rad) * _radius);
            }
        }
    }
}
