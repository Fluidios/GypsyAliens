using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Short-lived noise burst (e.g. rock landing) with a white expanding ripple.
    /// </summary>
    public sealed class NoisePulse : NoiseSource
    {
        public const float DefaultDuration = 1f;

        float _lifeLeft;
        float _duration = DefaultDuration;

        public void Configure(float radius, float duration, float loudness)
        {
            _radius = Mathf.Max(0.05f, radius);
            _duration = Mathf.Max(0.15f, duration);
            _lifeLeft = _duration;
            _loudness = Mathf.Clamp01(loudness);

            // Visual only — do not use static concentric NoiseFloorView.
            if (FloorView != null)
            {
                FloorView.SetVisible(false);
            }

            var pos = transform.position;
            pos.y = 0.05f;
            NoiseRippleEffect.Play(pos, _radius, _duration);
            NoiseRegistry.Register(this);
        }

        public override bool IsAudible => _lifeLeft > 0f && _radius > 0.05f;

        protected override void OnEnable()
        {
            NoiseRegistry.Register(this);
            // Skip creating floor view for pulses — ripple handles visuals.
        }

        void Update()
        {
            _lifeLeft -= Time.deltaTime;
            if (_lifeLeft <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
