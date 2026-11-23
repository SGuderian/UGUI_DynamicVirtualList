using UnityEngine;
using UnityEngine.UI;

namespace VirtualList
{
    public enum MeasureAxis
    {
        /// <summary>高度动态（宽度由列表/网格锁定），只测高度。</summary>
        DynamicHeight = 0,
        /// <summary>宽度动态（高度锁定），只测宽度。</summary>
        DynamicWidth = 1,
        /// <summary>宽高都由自动布局决定。</summary>
        DynamicBoth = 2,
    }

    public static class AutoLayoutMeasure
    {
        /// <summary>在给定尺寸约束下读取 UGUI 首选尺寸，测量后恢复根节点尺寸。</summary>
        /// <param name="rect">单元格的根节点。</param>
        /// <param name="axis">需要测量的方向。</param>
        /// <param name="fixedSize">另一方向的约束尺寸。双向测量时忽略。</param>
        /// <param name="forceRebuild">如果刚更新布局内容，可在读取前立即重建该节点的布局。</param>
        /// <param name="fitter">可选的已启用尺寸适配器。设置为 MinSize 的方向使用布局最小尺寸。</param>
        public static Vector2 Measure(RectTransform rect, MeasureAxis axis, float fixedSize,
            bool forceRebuild = true, ContentSizeFitter fitter = null)
        {
            if (rect == null) return Vector2.zero;

            Vector2 original = rect.sizeDelta;
            try
            {
                if (axis == MeasureAxis.DynamicHeight && !rect.rect.width.Equals(fixedSize))
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fixedSize);
                else if (axis == MeasureAxis.DynamicWidth && !rect.rect.height.Equals(fixedSize))
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fixedSize);

                if (forceRebuild) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

                bool useFitter = fitter != null && fitter.isActiveAndEnabled;
                bool useMinWidth = useFitter && fitter.horizontalFit == ContentSizeFitter.FitMode.MinSize;
                bool useMinHeight = useFitter && fitter.verticalFit == ContentSizeFitter.FitMode.MinSize;
                float width = axis == MeasureAxis.DynamicHeight ? fixedSize : ReadSize(rect, 0, useMinWidth);
                float height = axis == MeasureAxis.DynamicWidth ? fixedSize : ReadSize(rect, 1, useMinHeight);
                return new Vector2(width, height);
            }
            finally
            {
                if (rect != null && !rect.sizeDelta.Equals(original)) rect.sizeDelta = original;
            }
        }

        private static float ReadSize(RectTransform rect, int axis, bool useMinimum)
        {
            return useMinimum ? LayoutUtility.GetMinSize(rect, axis) : LayoutUtility.GetPreferredSize(rect, axis);
        }

        /// <summary>按给定宽度测量条目高度。</summary>
        public static float MeasureHeight(RectTransform rect, float width, bool forceRebuild = true,
            ContentSizeFitter fitter = null)
        {
            return Measure(rect, MeasureAxis.DynamicHeight, width, forceRebuild, fitter).y;
        }

        /// <summary>按给定高度测量条目宽度。</summary>
        public static float MeasureWidth(RectTransform rect, float height, bool forceRebuild = true,
            ContentSizeFitter fitter = null)
        {
            return Measure(rect, MeasureAxis.DynamicWidth, height, forceRebuild, fitter).x;
        }
    }
}
