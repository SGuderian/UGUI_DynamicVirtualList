using UnityEngine;

namespace VirtualList
{
    internal enum MotionState
    {
        Idle,
        Dragging,
        Inertia,
        SpringBack,
        Snapping,
        Tween,
    }

    /// <summary>返回最接近当前主轴偏移的吸附位置，并输出目标条目索引。没有目标时索引为 -1。</summary>
    public delegate float SnapResolver(float currentOffset, SnapAlign align, out int itemIndex);

    internal sealed class ScrollMotion
    {
        public ScrollSettings Settings;
        public MotionState State { get; private set; } = MotionState.Idle;
        public bool Looping { get; set; }

        // 主轴速度以 UGUI 布局单位每秒计。
        private float _velocity;
        private Vector2 _origin;
        private Vector2 _contentSize;
        private Vector2 _viewportSize;
        private bool _vertical;

        private bool _springToStart;

        private float _tweenFrom;
        private float _tweenTo;
        private float _tweenElapsed;
        private float _tweenDuration;
        private EaseCurve _tweenCurve;

        public bool IsAnimating => State == MotionState.Inertia
                                 || State == MotionState.SpringBack
                                 || State == MotionState.Snapping
                                 || State == MotionState.Tween;

        public ScrollMotion(ScrollSettings settings) => Settings = settings;

        public void Bind(Vector2 origin, Vector2 contentSize, Vector2 viewportSize, bool vertical)
        {
            _origin = origin;
            _contentSize = contentSize;
            _viewportSize = viewportSize;
            _vertical = vertical;
        }

        private float OriginAxis
        {
            get => _vertical ? _origin.y : _origin.x;
            set
            {
                if (_vertical) _origin.y = value; else _origin.x = value;
            }
        }

        private float MinAxis => 0f;
        private float MaxAxis => Mathf.Max(0f, (_vertical ? _contentSize.y : _contentSize.x)
                                             - (_vertical ? _viewportSize.y : _viewportSize.x));

        public Vector2 Origin => _origin;

        public void BeginDrag()
        {
            State = MotionState.Dragging;
            _velocity = 0f;
        }

        public Vector2 Drag(Vector2 delta, float dt)
        {
            State = MotionState.Dragging;
            float axisDelta = _vertical ? delta.y : delta.x;
            axisDelta *= Settings.DragSensitivity;

            float current = OriginAxis;
            float next = current + axisDelta;
            float boundary = Mathf.Clamp(next, MinAxis, MaxAxis);
            if (!Looping && !Settings.RubberbandEnabled)
                next = boundary;
            else if (!Looping && next != boundary)
            {
                float currentOver = current - Mathf.Clamp(current, MinAxis, MaxAxis);
                float nextOver = next - boundary;
                float existing = Mathf.Sign(nextOver) == Mathf.Sign(currentOver) ? currentOver : 0f;
                if (Mathf.Abs(nextOver) > Mathf.Abs(existing))
                    next = boundary + existing + (nextOver - existing) * Mathf.Lerp(1f,
                        1f - Mathf.Clamp01(Settings.RubberbandStiffness), Mathf.Abs(nextOver) / 200f);
            }

            OriginAxis = next;
            float limit = Mathf.Max(0f, Settings.MaxInertiaSpeed);
            if (dt > 0f) _velocity = Mathf.Clamp((next - current) / dt, -limit, limit);
            return _origin;
        }

        public void EndDrag(bool useVelocity)
        {
            if (IsOutOfBounds())
            {
                StartSpring();
                return;
            }
            if (!useVelocity || !Settings.InertiaEnabled || Mathf.Abs(_velocity) <= StopSpeed)
            {
                _velocity = 0f;
                State = ResolveSnapState();
                return;
            }
            State = MotionState.Inertia;
        }

        public Vector2 Update(float dt, SnapResolver resolver, out bool stillMoving)
        {
            if (State == MotionState.Snapping) StartSnap(resolver);

            switch (State)
            {
                case MotionState.Inertia:
                    TickInertia(dt);
                    break;
                case MotionState.SpringBack:
                    TickSpring(dt);
                    break;
                case MotionState.Snapping:
                    TickTween(dt);
                    break;
                case MotionState.Tween:
                    TickTween(dt);
                    break;
                case MotionState.Dragging:
                case MotionState.Idle:
                    break;
            }

            stillMoving = IsAnimating;
            return _origin;
        }

        private void TickInertia(float dt)
        {
            // InertiaDamping 以 60 Hz 的单帧为基准。
            float damping = Mathf.Pow(Mathf.Clamp(Settings.InertiaDamping, 0f, 0.9999f), dt * 60f);
            _velocity *= damping;

            OriginAxis += _velocity * dt;

            if (IsOutOfBounds())
            {
                if (Settings.RubberbandEnabled) StartSpring();
                else
                {
                    OriginAxis = Mathf.Clamp(OriginAxis, MinAxis, MaxAxis);
                    _velocity = 0f;
                    State = ResolveSnapState();
                }
                return;
            }

            float threshold = Settings.Snap == SnapMode.Continuous
                ? Mathf.Max(StopSpeed, Settings.SnapVelocityThreshold) : StopSpeed;
            if (Mathf.Abs(_velocity) <= threshold)
            {
                _velocity = 0f;
                State = ResolveSnapState();
            }
        }

        private float StopSpeed => Mathf.Max(0.01f, Settings.InertiaStopSpeed);

        private void StartSpring()
        {
            _springToStart = OriginAxis < MinAxis;
            _velocity = 0f;
            State = MotionState.SpringBack;
        }

        private void TickSpring(float dt)
        {
            if (dt <= 0f) return;
            float goal = _springToStart ? MinAxis : MaxAxis;
            float smoothTime = 2f / Mathf.Sqrt(Mathf.Max(1f, Settings.SpringStrength));
            float current = Mathf.SmoothDamp(OriginAxis, goal, ref _velocity, smoothTime, Mathf.Infinity, dt);
            OriginAxis = current;

            if (Mathf.Abs(goal - current) < 0.5f && Mathf.Abs(_velocity) < 1f)
            {
                OriginAxis = goal;
                _velocity = 0f;
                State = ResolveSnapState();
            }
        }

        private void TickTween(float dt)
        {
            _tweenElapsed += dt;
            float t = _tweenDuration <= 0f ? 1f : Mathf.Clamp01(_tweenElapsed / _tweenDuration);
            float eased = Evaluate(_tweenCurve, t);
            OriginAxis = Mathf.LerpUnclamped(_tweenFrom, _tweenTo, eased);

            if (t >= 1f)
            {
                OriginAxis = _tweenTo;
                State = MotionState.Idle;
            }
        }

        private MotionState ResolveSnapState()
        {
            if (Settings.Snap == SnapMode.OnStop || Settings.Snap == SnapMode.Continuous)
                return MotionState.Snapping;
            return MotionState.Idle;
        }

        public void StartSnap(SnapResolver resolver)
        {
            State = MotionState.Idle;
            if (resolver == null || Settings.Snap == SnapMode.None) return;
            float target = resolver(OriginAxis, Settings.SnapAlign, out int index);
            if (!Looping) target = Mathf.Clamp(target, MinAxis, MaxAxis);
            if (index < 0 || Mathf.Abs(target - OriginAxis) < 0.01f) return;
            StartTween(target, Settings.SnapDuration, EaseCurve.EaseOutCubic);
        }

        public void ScrollTo(float targetOffset, bool smooth, float duration, EaseCurve curve)
        {
            if (!Looping) targetOffset = Mathf.Clamp(targetOffset, MinAxis, MaxAxis);
            if (!smooth || targetOffset == OriginAxis)
            {
                OriginAxis = targetOffset;
                CancelMotion();
                return;
            }
            StartTween(targetOffset, duration, curve);
        }

        private void StartTween(float target, float duration, EaseCurve curve)
        {
            _tweenFrom = OriginAxis;
            _tweenTo = target;
            _tweenElapsed = 0f;
            _tweenDuration = Mathf.Max(0.001f, duration);
            _tweenCurve = curve;
            State = MotionState.Tween;
            _velocity = 0f;
        }

        public void RetargetTween(float target)
        {
            // 目标条目尺寸变化时，从当前位置重新补间，沿用剩余时长。
            if (!Looping) target = Mathf.Clamp(target, MinAxis, MaxAxis);
            if (target == _tweenTo) return;
            StartTween(target, _tweenDuration - _tweenElapsed, _tweenCurve);
        }

        private bool IsOutOfBounds()
        {
            float a = OriginAxis;
            return !Looping && (a < MinAxis - 0.01f || a > MaxAxis + 0.01f);
        }

        public void ShiftOrigin(float delta)
        {
            // 跨循环接缝时，位置与补间端点一起换算到新一轮。
            OriginAxis += delta;
            if (State != MotionState.Tween) return;
            _tweenFrom += delta;
            _tweenTo += delta;
        }

        public void CancelMotion()
        {
            State = MotionState.Idle;
            _velocity = 0f;
        }

        public static float Evaluate(EaseCurve curve, float t)
        {
            switch (curve)
            {
                case EaseCurve.EaseOutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case EaseCurve.EaseOutQuart: return 1f - Mathf.Pow(1f - t, 4f);
                case EaseCurve.EaseInOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case EaseCurve.Spring:
                    float c = 1.70158f;
                    return 1f + (c + 1f) * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f);
                default: return t;
            }
        }
    }
}
