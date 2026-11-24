using UnityEngine;

namespace VirtualList
{
    public enum VirtualLayoutMode
    {
        VerticalList = 0,
        HorizontalList = 1,
        /// <summary>固定列数，行高取该行条目中的最大高度。</summary>
        Grid = 2,
        /// <summary>按业务提供的显示序列和深度排列树节点。</summary>
        Tree = 3,
    }

    [System.Serializable]
    public struct LayoutOptions
    {
        public VirtualLayoutMode Mode;

        /// <summary>主轴上的条目间距。网格同时用作列间距。</summary>
        public float Spacing;

        public RectOffset Padding;

        /// <summary>条目没有有效估算时使用的尺寸。内置布局只使用主轴分量。</summary>
        public Vector2 DefaultItemSize;

        /// <summary>Grid 模式列数。</summary>
        public int ColumnCount;

        /// <summary>Tree 模式每级缩进。</summary>
        public float IndentPerDepth;

        public static LayoutOptions Default => new LayoutOptions
        {
            Mode = VirtualLayoutMode.VerticalList,
            Spacing = 4f,
            Padding = new RectOffset(0, 0, 0, 0),
            DefaultItemSize = Vector2.one * SizePrefixCache.DefaultLength,
            ColumnCount = 2,
            IndentPerDepth = 24f,
        };
    }

    [System.Serializable]
    public struct RectOffset
    {
        public int Left, Right, Top, Bottom;
        public RectOffset(int l, int r, int t, int b) { Left = l; Right = r; Top = t; Bottom = b; }
    }
}
