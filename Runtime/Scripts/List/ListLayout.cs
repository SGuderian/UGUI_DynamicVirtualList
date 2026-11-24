using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    public sealed class ListLayout : IVirtualLayout, IBufferedVirtualLayout
    {
        private readonly SizePrefixCache _prefix = new SizePrefixCache();
        private LayoutOptions _options;
        private Vector2 _viewport;
        private Vector2[] _sizes;          // 每个 item 缓存尺寸
        private int[] _templateIds;
        private readonly bool _vertical;

        private float LeadingPadding => _vertical ? _options.Padding.Top : _options.Padding.Left;
        private float TrailingPadding => _vertical ? _options.Padding.Bottom : _options.Padding.Right;
        private float CrossSize => Mathf.Max(0f, _vertical
            ? _viewport.x - _options.Padding.Left - _options.Padding.Right
            : _viewport.y - _options.Padding.Top - _options.Padding.Bottom);

        public LayoutOptions Options => _options;
        public Vector2 ContentSize { get; private set; }
        public int Count => _sizes == null ? 0 : _sizes.Length;
        public bool IsVertical => _vertical;

        public ListLayout(bool vertical) => _vertical = vertical;

        public void Rebuild(int count, LayoutOptions options, Vector2 viewportSize,
            Func<int, Vector2> estimateSize = null, Func<int, int> templateId = null, Func<int, int> depth = null)
        {
            _options = options;
            _options.Spacing = SizePrefixCache.NonNegative(options.Spacing);
            _viewport = viewportSize;
            int n = count;

            if (_sizes == null || _sizes.Length != n)
            {
                _sizes = new Vector2[n];
                _templateIds = new int[n];
            }

            float crossSize = CrossSize;
            float defaultLength = _vertical ? options.DefaultItemSize.y : options.DefaultItemSize.x;
            for (int i = 0; i < n; i++)
            {
                var estimate = (estimateSize?.Invoke(i) ?? Vector2.zero);
                _sizes[i] = _vertical
                    ? new Vector2(crossSize, SizePrefixCache.Estimate(estimate.y, defaultLength))
                    : new Vector2(SizePrefixCache.Estimate(estimate.x, defaultLength), crossSize);
                _templateIds[i] = templateId?.Invoke(i) ?? 0;
            }

            _prefix.Init(n, _options.Spacing, index => _vertical ? _sizes[index].y : _sizes[index].x);
            RefreshContentSize();
        }

        public void FillVisible(Rect visibleRect, List<VirtualItemData> result)
        {
            result.Clear();
            FillVisible(visibleRect, new VisibleItemWriter(result));
        }

        void IBufferedVirtualLayout.FillVisible(Rect visibleRect, VisibleItemWriter writer) => FillVisible(visibleRect, writer);

        int IBufferedVirtualLayout.GetFirstIndexAtOffset(float offset) => _prefix.FirstIndexAtOrAfter(offset - LeadingPadding);

        private void FillVisible(Rect visibleRect, VisibleItemWriter writer)
        {
            int n = Count;
            if (n == 0) return;

            float origin = (_vertical ? visibleRect.yMin : visibleRect.xMin) - LeadingPadding;
            float extent = (_vertical ? visibleRect.yMax : visibleRect.xMax) - LeadingPadding;

            int start = _prefix.IndexAtOffset(origin);
            double cursor = _prefix.PrefixLength(start);
            for (int i = start; i < n; i++)
            {
                Vector2 size = _sizes[i];
                float axisLen = _vertical ? size.y : size.x;
                float axisStart = (float)cursor;
                double axisEnd = cursor + axisLen;

                if (axisStart >= extent) break;
                if (axisEnd > origin)
                {
                    Rect rect = _vertical
                        ? RectWithPadding(size.x, size.y, 0f, axisStart)
                        : RectWithPadding(size.x, size.y, axisStart, 0f);

                    writer.Add(i, _templateIds[i], rect, size);
                }

                cursor = axisEnd + _options.Spacing;
            }
        }

        public bool ResizeItem(int visibleIndex, Vector2 realSize)
        {
            if (visibleIndex < 0 || visibleIndex >= Count) return false;
            Vector2 old = _sizes[visibleIndex];
            float newAxis = _vertical ? realSize.y : realSize.x;
            float oldAxis = _vertical ? old.y : old.x;
            if (!SizePrefixCache.IsValidLength(newAxis) || Mathf.Approximately(oldAxis, newAxis)) return false;

            _sizes[visibleIndex] = _vertical
                ? new Vector2(old.x, newAxis)
                : new Vector2(newAxis, old.y);
            _prefix.SetLength(visibleIndex, newAxis);
            RefreshContentSize();
            return true;
        }

        public Rect GetRect(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= Count) return default;
            float axisStart = _prefix.Prefix(visibleIndex);
            Vector2 size = _sizes[visibleIndex];
            return _vertical
                ? RectWithPadding(size.x, size.y, 0f, axisStart)
                : RectWithPadding(size.x, size.y, axisStart, 0f);
        }

        public float GetScrollOffsetForIndex(int visibleIndex) => LeadingPadding + _prefix.Prefix(visibleIndex);

        private Rect RectWithPadding(float w, float h, float x, float y)
        {
            var p = _options.Padding;
            return _vertical
                ? new Rect(p.Left, p.Top + y, w, h)
                : new Rect(p.Left + x, p.Top, w, h);
        }

        private void RefreshContentSize()
        {
            float total = Count == 0 ? 0f : Mathf.Max(0f, LeadingPadding + _prefix.TotalLength + TrailingPadding);
            float cross = _vertical ? _viewport.x : _viewport.y;
            ContentSize = _vertical ? new Vector2(cross, total) : new Vector2(total, cross);
        }
    }
}
