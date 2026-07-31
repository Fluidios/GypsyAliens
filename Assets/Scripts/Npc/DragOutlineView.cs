using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Yellow ring under an NPC while the player is dragging it.
    /// </summary>
    public sealed class DragOutlineView : MonoBehaviour
    {
        [SerializeField] float _radius = 0.55f;
        [SerializeField] float _height = 0.06f;
        [SerializeField] int _segments = 28;
        [SerializeField] Color _color = new Color(1f, 0.9f, 0.15f, 0.95f);
        [SerializeField] float _width = 0.07f;

        LineRenderer _line;
        bool _visible;

        void Awake()
        {
            EnsureLine();
            SetVisible(false);
        }

        void EnsureLine()
        {
            if (_line != null)
            {
                return;
            }

            _line = gameObject.AddComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = false;
            _line.positionCount = _segments;
            _line.startWidth = _width;
            _line.endWidth = _width;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.allowOcclusionWhenDynamic = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", _color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.color = _color;
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            mat.renderQueue = 3000;
            _line.sharedMaterial = mat;

            for (var i = 0; i < _segments; i++)
            {
                var a = i / (float)_segments * Mathf.PI * 2f;
                _line.SetPosition(i, new Vector3(Mathf.Cos(a) * _radius, _height, Mathf.Sin(a) * _radius));
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            EnsureLine();
            _line.enabled = visible;
        }

        public void ConfigureRadius(float radius)
        {
            _radius = Mathf.Max(0.2f, radius);
            if (_line == null)
            {
                return;
            }

            for (var i = 0; i < _segments; i++)
            {
                var a = i / (float)_segments * Mathf.PI * 2f;
                _line.SetPosition(i, new Vector3(Mathf.Cos(a) * _radius, _height, Mathf.Sin(a) * _radius));
            }
        }
    }
}
