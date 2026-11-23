using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    /// <summary>按显示索引计算条目几何。内容左上角为原点，x 向右，y 向下。</summary>
    public interface IVirtualLayout
    {
        LayoutOptions Options { get; }

        /// <summary>布局中的条目总数，含当前视口之外的条目。</summary>
        int Count { get; }

        /// <summary>纵向布局取 y 为主轴，横向布局取 x。</summary>
        bool IsVertical { get; }

        /// <summary>按新的显示序列和视口尺寸重建布局缓存。可选回调均以显示索引取值。</summary>
        void Rebuild(int count, LayoutOptions options, Vector2 viewportSize,
            Func<int, Vector2> estimateSize = null, Func<int, int> templateId = null, Func<int, int> depth = null);

        /// <summary>含内边距的逻辑内容尺寸，用于约束滚动范围。</summary>
        Vector2 ContentSize { get; }

        /// <summary>清空 result，按显示顺序写入与逻辑视口相交的条目。</summary>
        void FillVisible(Rect visibleRect, List<VirtualItemData> result);

        /// <summary>更新某个显示索引的实测尺寸。条目几何变化时返回 true。</summary>
        bool ResizeItem(int visibleIndex, Vector2 realSize);

        /// <summary>获取条目当前使用的逻辑矩形。</summary>
        Rect GetRect(int visibleIndex);

        /// <summary>返回将条目前沿对齐到视口起点所需的主轴偏移。</summary>
        float GetScrollOffsetForIndex(int visibleIndex);
    }

    // 滚动路径复用可见项缓冲区。
    internal interface IBufferedVirtualLayout
    {
        void FillVisible(Rect visibleRect, VisibleItemWriter writer);
    }

    // 网格在批次结束时统一更新受影响的行高。
    internal interface IBatchedVirtualLayout
    {
        void BeginResizeBatch();
        bool EndResizeBatch();
    }
}
