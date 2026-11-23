using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    public sealed class TreeLayout : IVirtualLayout, IBufferedVirtualLayout
    {
        private readonly SizePrefixCache _prefix = new SizePrefixCache();
        private LayoutOptions _options;
        private Vector2 _viewport;
        private int _count;
        private Vector2[] _sizes;
        private int[] _templateIds;
        private int[] _depthOf;

        public LayoutOptions Options => _options;
        public Vector2 ContentSize { get; private set; }
        public int Count => _count;
        public bool IsVertical => true;

        public void Rebuild(int count, LayoutOptions options, Vector2 viewportSize,
            Func<int, Vector2> estimateSize = null, Func<int, int> templateId = null, Func<int, int> depth = null)
        {
            _count = count;
            _options = options;
            _options.Spacing = SizePrefixCache.NonNegative(options.Spacing);
            _options.IndentPerDepth = SizePrefixCache.NonNegative(options.IndentPerDepth);
            _viewport = viewportSize;
            if (_sizes == null || _sizes.Length < count)
            {
                _sizes = new Vector2[count];
                _templateIds = new int[count];
                _depthOf = new int[count];
            }

            for (int i = 0; i < count; i++)
            {
                _depthOf[i] = Mathf.Max(0, depth?.Invoke(i) ?? 0);
                _sizes[i] = new Vector2(GetWidth(i), SizePrefixCache.Estimate((estimateSize?.Invoke(i) ?? Vector2.zero).y));
                _templateIds[i] = templateId?.Invoke(i) ?? 0;
            }

            _prefix.Init(count, _options.Spacing, index => _sizes[index].y);
            RefreshContentSize();
        }

        public void FillVisible(Rect visibleRect, List<VirtualItemData> result)
        {
            result.Clear();
            FillVisible(visibleRect, new VisibleItemWriter(result));
        }

        void IBufferedVirtualLayout.FillVisible(Rect visibleRect, VisibleItemWriter writer) => FillVisible(visibleRect, writer);

        private void FillVisible(Rect visibleRect, VisibleItemWriter writer)
        {
            int n = Count;
            if (n == 0) return;

            var p = _options.Padding;
            float origin = visibleRect.yMin - p.Top;
            float extent = visibleRect.yMax - p.Top;

            int start = _prefix.IndexAtOffset(origin);
            double cursor = _prefix.PrefixLength(start);

            for (int i = start; i < n; i++)
            {
                float h = _sizes[i].y;
                if ((float)cursor >= extent) break;
                if (cursor + h > origin)
                {
                    Rect rect = new Rect(GetIndent(i), p.Top + (float)cursor, _sizes[i].x, h);

                    writer.Add(i, _templateIds[i], rect, _sizes[i], _depthOf[i]);
                }
                cursor += (double)h + _options.Spacing;
            }
        }

        public bool ResizeItem(int visibleIndex, Vector2 realSize)
        {
            if (visibleIndex < 0 || visibleIndex >= Count || !SizePrefixCache.IsValidLength(realSize.y)) return false;
            Vector2 old = _sizes[visibleIndex];
            if (Mathf.Approximately(old.y, realSize.y)) return false;
            _sizes[visibleIndex] = new Vector2(old.x, realSize.y);
            _prefix.SetLength(visibleIndex, realSize.y);
            RefreshContentSize();
            return true;
        }

        public Rect GetRect(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= Count) return default;
            return new Rect(new Vector2(GetIndent(visibleIndex), GetScrollOffsetForIndex(visibleIndex)), _sizes[visibleIndex]);
        }

        public float GetScrollOffsetForIndex(int visibleIndex) => _options.Padding.Top + _prefix.Prefix(visibleIndex);

        private float GetIndent(int index) => _options.Padding.Left + _depthOf[index] * _options.IndentPerDepth;

        private float GetWidth(int index) => Mathf.Max(0f, _viewport.x - GetIndent(index) - _options.Padding.Right);

        private void RefreshContentSize()
        {
            var p = _options.Padding;
            ContentSize = new Vector2(_viewport.x, Count == 0 ? 0f : Mathf.Max(0f, p.Top + _prefix.TotalLength + p.Bottom));
        }
    }
}
