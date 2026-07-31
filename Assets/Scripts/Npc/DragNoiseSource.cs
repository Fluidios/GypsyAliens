using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Continuous drag noise — audible only while the animal is being moved.
    /// Visual: looping white ripple like the click-to-move effect.
    /// </summary>
    public sealed class DragNoiseSource : NoiseSource
    {
        bool _emitting;
        NoiseRippleEffect _loopRipple;

        public override bool IsAudible => _emitting && isActiveAndEnabled && _radius > 0.05f;

        protected override void OnEnable()
        {
            NoiseRegistry.Register(this);
            if (FloorView != null)
            {
                FloorView.SetVisible(false);
            }
        }

        public void SetEmitting(bool emitting)
        {
            if (_emitting == emitting)
            {
                return;
            }

            _emitting = emitting;
            if (_emitting)
            {
                EnsureLoopRipple();
                _loopRipple.gameObject.SetActive(true);
                _loopRipple.ConfigureLooping(_radius, NoisePulse.DefaultDuration);
                _loopRipple.Restart();
            }
            else if (_loopRipple != null)
            {
                _loopRipple.gameObject.SetActive(false);
            }
        }

        protected override void OnRadiusChanged()
        {
            // Ripple visual only — no static floor rings.
            if (_emitting && _loopRipple != null)
            {
                _loopRipple.ConfigureLooping(_radius, NoisePulse.DefaultDuration);
            }
        }

        void LateUpdate()
        {
            if (!_emitting || _loopRipple == null)
            {
                return;
            }

            var pos = transform.position;
            pos.y = 0.05f;
            _loopRipple.transform.position = pos;
            _loopRipple.ConfigureLooping(_radius, NoisePulse.DefaultDuration);
        }

        void EnsureLoopRipple()
        {
            if (_loopRipple != null)
            {
                return;
            }

            var pos = transform.position;
            pos.y = 0.05f;
            _loopRipple = NoiseRippleEffect.Play(pos, _radius, NoisePulse.DefaultDuration);
            _loopRipple.ConfigureLooping(_radius, NoisePulse.DefaultDuration);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_loopRipple != null)
            {
                Destroy(_loopRipple.gameObject);
                _loopRipple = null;
            }
        }
    }
}
