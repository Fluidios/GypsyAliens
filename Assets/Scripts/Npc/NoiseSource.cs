using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Base audible noise with optional floor ring visualization.
    /// </summary>
    public abstract class NoiseSource : MonoBehaviour
    {
        [SerializeField] protected float _radius = 3.5f;
        [SerializeField] protected float _loudness = 1f;

        protected NoiseFloorView FloorView;

        public virtual bool IsAudible => isActiveAndEnabled && _radius > 0.05f;
        public virtual Vector3 WorldPosition => transform.position;
        public float Radius => _radius;
        public float Loudness => _loudness;

        protected virtual void OnEnable()
        {
            NoiseRegistry.Register(this);
            EnsureFloorView();
            RefreshFloorView();
        }

        protected virtual void OnDisable()
        {
            NoiseRegistry.Unregister(this);
            if (FloorView != null)
            {
                FloorView.SetVisible(false);
            }
        }

        protected virtual void OnDestroy()
        {
            NoiseRegistry.Unregister(this);
        }

        protected void EnsureFloorView()
        {
            if (FloorView != null)
            {
                return;
            }

            var go = new GameObject("NoiseFloor");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            FloorView = go.AddComponent<NoiseFloorView>();
        }

        protected void RefreshFloorView()
        {
            EnsureFloorView();
            FloorView.Configure(_radius, _loudness);
            FloorView.SetVisible(IsAudible);
        }

        public void SetRadius(float radius, float loudness = -1f)
        {
            _radius = Mathf.Max(0.05f, radius);
            if (loudness >= 0f)
            {
                _loudness = Mathf.Clamp01(loudness);
            }

            OnRadiusChanged();
        }

        protected virtual void OnRadiusChanged()
        {
            RefreshFloorView();
        }
    }
}
