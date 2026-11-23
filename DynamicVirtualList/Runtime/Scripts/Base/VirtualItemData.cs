using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    /// <summary>条目在内容坐标系中的布局快照。</summary>
    public sealed class VirtualItemData
    {
        /// <summary>业务提供的显示序列索引。</summary>
        public int Index;

        internal long Cycle;

        /// <summary>对应的模板 ID。</summary>
        public int TemplateId;

        /// <summary>实际占用的逻辑矩形。网格中高度为整行的高度。</summary>
        public Rect Rect;

        /// <summary>条目自身的实测或估算尺寸。</summary>
        public Vector2 Size;

        /// <summary>树布局中的缩进深度。</summary>
        public int Depth;

        public VirtualItemData(int index, int templateId)
        {
            Index = index;
            TemplateId = templateId;
        }
    }

    internal sealed class VisibleItemBuffer
    {
        private readonly List<VirtualItemData> _items = new List<VirtualItemData>();

        public int Count { get; private set; }
        public VirtualItemData this[int index] => _items[index];

        public void Clear() => Count = 0;

        public VirtualItemData AddSlot(int index, int templateId)
        {
            if (Count == _items.Count) _items.Add(new VirtualItemData(index, templateId));
            return _items[Count++];
        }
    }

    internal readonly struct VisibleItemWriter
    {
        private readonly VisibleItemBuffer _buffer;
        private readonly List<VirtualItemData> _snapshots;
        private readonly Vector2 _shift;
        private readonly long _cycle;

        public VisibleItemWriter(List<VirtualItemData> snapshots)
        {
            _snapshots = snapshots;
            _buffer = null;
            _shift = Vector2.zero;
            _cycle = 0;
        }

        public VisibleItemWriter(VisibleItemBuffer buffer, Vector2 shift, long cycle)
        {
            _buffer = buffer;
            _snapshots = null;
            _shift = shift;
            _cycle = cycle;
        }

        public void Add(int index, int templateId, Rect rect, Vector2 size, int depth = 0)
        {
            var item = _buffer?.AddSlot(index, templateId) ?? new VirtualItemData(index, templateId);
            rect.position += _shift;
            item.Index = index;
            item.TemplateId = templateId;
            item.Rect = rect;
            item.Size = size;
            item.Cycle = _cycle;
            item.Depth = depth;
            _snapshots?.Add(item);
        }
    }
}
