using UnityEngine;

namespace VirtualList
{
    public enum SnapAlign
    {
        /// <summary>条目前沿对齐视口起点。横向时对齐左侧。</summary>
        Top = 0,
        /// <summary>条目中心对齐视口中心。</summary>
        Center = 1,
    }

    public enum SnapMode
    {
        /// <summary>不吸附。</summary>
        None = 0,
        /// <summary>惯性停下后吸附到最近条目。</summary>
        OnStop = 1,
        /// <summary>惯性速度降至吸附阈值时开始吸附。</summary>
        Continuous = 2,
    }

    public enum EaseCurve
    {
        Linear = 0,
        EaseOutCubic = 1,
        EaseOutQuart = 2,
        EaseInOutCubic = 3,
        Spring = 4,
    }

    [System.Serializable]
    public struct ScrollSettings
    {
        [Header("Dragging")]
        [Tooltip("拖拽灵敏度倍率，1 为 1:1 跟随指针。")]
        public float DragSensitivity;

        [Header("Inertia 惯性")]
        [Tooltip("松手后是否保留动量继续滚动。")]
        public bool InertiaEnabled;
        [Tooltip("惯性速度每帧衰减系数（0~1，越接近 1 滑得越远）。")]
        public float InertiaDamping;
        [Tooltip("惯性最大速度（像素/秒），防止甩飞。")]
        public float MaxInertiaSpeed;
        [Tooltip("低于该速度（像素/秒）视为惯性停止。")]
        public float InertiaStopSpeed;

        [Header("Rubberband 越界回弹")]
        [Tooltip("是否允许拖出边界并回弹。")]
        public bool RubberbandEnabled;
        [Tooltip("越界时的阻力系数（越大越难拖出去）。")]
        public float RubberbandStiffness;
        [Tooltip("回弹弹簧强度。")]
        public float SpringStrength;

        [Header("Snap 吸附")]
        public SnapMode Snap;
        public SnapAlign SnapAlign;
        [Tooltip("吸附补间时长（秒）。")]
        public float SnapDuration;
        [Tooltip("连续吸附时的速度阈值，低于才触发对齐。")]
        public float SnapVelocityThreshold;

        [Header("Tween 平滑滚动")]
        [Tooltip("ScrollToIndex 是否用补间动画（false 为瞬间跳转）。")]
        public bool SmoothScroll;
        public float SmoothDuration;
        public EaseCurve SmoothCurve;

        public static ScrollSettings Default => new ScrollSettings
        {
            DragSensitivity = 1f,

            InertiaEnabled = true,
            InertiaDamping = 0.92f,
            MaxInertiaSpeed = 2400f,
            InertiaStopSpeed = 80f,

            RubberbandEnabled = true,
            RubberbandStiffness = 0.5f,
            SpringStrength = 12f,

            Snap = SnapMode.None,
            SnapAlign = SnapAlign.Top,
            SnapDuration = 0.25f,
            SnapVelocityThreshold = 500f,

            SmoothScroll = true,
            SmoothDuration = 0.35f,
            SmoothCurve = EaseCurve.EaseOutCubic,
        };
    }
}
