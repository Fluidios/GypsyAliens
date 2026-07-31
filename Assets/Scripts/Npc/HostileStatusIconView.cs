using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// World-space ? / ! billboard above the hostile for awareness / combat states.
    /// </summary>
    public sealed class HostileStatusIconView : MonoBehaviour
    {
        public enum IconKind : byte
        {
            None = 0,
            Question = 1,
            Exclamation = 2,
        }

        [SerializeField] float _height = 2.15f;
        [SerializeField] float _fontSize = 4.5f;
        [SerializeField] Color _questionColor = new Color(1f, 0.85f, 0.15f, 1f);
        [SerializeField] Color _exclamationColor = new Color(1f, 0.25f, 0.2f, 1f);

        TextMesh _text;
        Transform _billboard;
        IconKind _kind;

        void Awake()
        {
            EnsureText();
            SetIcon(IconKind.None);
        }

        void LateUpdate()
        {
            if (_billboard == null)
            {
                return;
            }

            _billboard.position = transform.position + Vector3.up * _height;

            var cam = Camera.main;
            if (cam != null)
            {
                _billboard.rotation = Quaternion.LookRotation(
                    _billboard.position - cam.transform.position,
                    Vector3.up);
            }
        }

        public void SetIcon(IconKind kind)
        {
            EnsureText();
            _kind = kind;
            if (_text == null)
            {
                return;
            }

            switch (kind)
            {
                case IconKind.Question:
                    _text.text = "?";
                    _text.color = _questionColor;
                    _billboard.gameObject.SetActive(true);
                    break;
                case IconKind.Exclamation:
                    _text.text = "!";
                    _text.color = _exclamationColor;
                    _billboard.gameObject.SetActive(true);
                    break;
                default:
                    _text.text = string.Empty;
                    _billboard.gameObject.SetActive(false);
                    break;
            }
        }

        void EnsureText()
        {
            if (_billboard != null)
            {
                return;
            }

            var go = new GameObject("StatusIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * _height;
            _billboard = go.transform;

            _text = go.AddComponent<TextMesh>();
            _text.alignment = TextAlignment.Center;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.characterSize = 0.12f;
            _text.fontSize = Mathf.RoundToInt(_fontSize * 10f);
            _text.fontStyle = FontStyle.Bold;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
