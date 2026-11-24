using UnityEngine;
using UnityEngine.UI;

namespace VirtualList
{
    /// <summary>从 UGUI 布局组件读取首选尺寸，业务测量回调可直接调用 GetPreferredSize。</summary>
    public class AutoLayoutCell : VirtualItemCell
    {
        [Tooltip("动态方向：高度动态（宽度被锁定）/ 宽度动态 / 宽高都动态。")]
        [SerializeField] private MeasureAxis _axis = MeasureAxis.DynamicHeight;

        [Tooltip("测量前是否强制立即重建布局。默认关闭：延迟测量时点布局已刷新，直接读缓存；" +
                 "仅当嵌套布局一帧内未稳定时才开启。")]
        [SerializeField] private bool _forceRebuildImmediate = false;

        [Tooltip("可选 ContentSizeFitter；留空自动从本节点获取。")]
        [SerializeField] private ContentSizeFitter _fitter;

        private RectTransform _rt;

        protected RectTransform CellRect => _rt != null ? _rt : (_rt = (RectTransform)transform);

        protected virtual void Awake()
        {
            if (_fitter == null) _fitter = GetComponent<ContentSizeFitter>();
        }

        /// <inheritdoc/>
        public override Vector2 GetPreferredSize()
        {
            if (CellRect == null) return Vector2.zero;

            // 使用布局分配的次轴尺寸作为约束，文本换行结果取决于这个宽度。
            float fixedWidth = CellRect.rect.width;
            float fixedHeight = CellRect.rect.height;
            float fixedSize = _axis == MeasureAxis.DynamicWidth ? fixedHeight : fixedWidth;

            return AutoLayoutMeasure.Measure(CellRect, _axis, fixedSize,
                _forceRebuildImmediate, _fitter);
        }

        /// <summary>更改单元格参与动态测量的方向。</summary>
        protected void SetMeasureAxis(MeasureAxis axis) => _axis = axis;
    }
}
