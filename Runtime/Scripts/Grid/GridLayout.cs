using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    public sealed class GridLayout : IVirtualLayout, IBufferedVirtualLayout, IBatchedVirtualLayout
    {
        private readonly SizePrefixCache _rowPrefix = new SizePrefixCache();
        private LayoutOptions _options;
        private Vector2 _viewport;
        private int _rows;
        private int _cols;
        private Vector2[] _sizes;
        private int[] _templateIds;
        private float[] _rowHeights;
        private float _cellWidth;
        private bool _batching;
        private readonly HashSet<int> _dirtyRows = new HashSet<int>();

        public LayoutOptions Options => _options;
        public Vector2 ContentSize { get; private set; }
        public int Count => _sizes == null ? 0 : _sizes.Length;
        public bool IsVertical => true;

        public void Rebuild(int count, LayoutOptions options, Vector2 viewportSize,
            Func<int, Vector2> estimateSize = null, Func<int, int> templateId = null, Func<int, int> depth = null)
        {
            _options = options;
            _options.Spacing = SizePrefixCache.NonNegative(options.Spacing);
            _viewport = viewportSize;
            _cols = Mathf.Max(1, options.ColumnCount);
            int n = count;
            _rows = n == 0 ? 0 : 1 + (n - 1) / _cols;

            if (_sizes == null || _sizes.Length != n)
            {
                _sizes = new Vector2[n];
                _templateIds = new int[n];
            }
            if (_rowHeights == null || _rowHeights.Length != _rows) _rowHeights = new float[_rows];
            else System.Array.Clear(_rowHeights, 0, _rows);

            var p = options.Padding;
            float availW = viewportSize.x - p.Left - p.Right - _options.Spacing * (_cols - 1);
            _cellWidth = Mathf.Max(0f, availW / _cols);

            for (int i = 0; i < n; i++)
            {
                float height = SizePrefixCache.Estimate((estimateSize?.Invoke(i) ?? Vector2.zero).y, options.DefaultItemSize.y);
                _sizes[i] = new Vector2(_cellWidth, height);
                _templateIds[i] = templateId?.Invoke(i) ?? 0;
                int row = i / _cols;
                _rowHeights[row] = Mathf.Max(_rowHeights[row], height);
            }

            _rowPrefix.Init(_rows, _options.Spacing, row => _rowHeights[row]);

            RefreshContentSize();
        }

        public void FillVisible(Rect visibleRect, List<VirtualItemData> result)
        {
            result.Clear();
            FillVisible(visibleRect, new VisibleItemWriter(result));
        }

        void IBufferedVirtualLayout.FillVisible(Rect visibleRect, VisibleItemWriter writer) => FillVisible(visibleRect, writer);

        int IBufferedVirtualLayout.GetFirstIndexAtOffset(float offset)
        {
            if (_cellWidth <= 0f) return -1;
            double stride = (double)_cellWidth + _options.Spacing;
            int column = GetFirstColumn(0f, stride);
            if (column >= _cols || _options.Padding.Left + column * stride >= _viewport.x) return -1;
            int row = _rowPrefix.FirstIndexAtOrAfter(offset - _options.Padding.Top);
            if (row < 0) return -1;
            int first = row * _cols;
            return column < Count - first ? first + column : -1;
        }

        private int GetFirstColumn(float minX, double stride)
            => (int)System.Math.Min(_cols, System.Math.Max(0d,
                System.Math.Floor(((double)minX - _options.Padding.Left - _cellWidth) / stride) + 1d));

        private void FillVisible(Rect visibleRect, VisibleItemWriter writer)
        {
            int n = Count;
            if (n == 0 || _rows == 0 || _cellWidth <= 0f || visibleRect.width <= 0f) return;

            var p = _options.Padding;
            double stride = (double)_cellWidth + _options.Spacing;
            int firstColumn = GetFirstColumn(visibleRect.xMin, stride);
            int endColumn = (int)System.Math.Min(_cols, System.Math.Max(0d,
                System.Math.Ceiling(((double)visibleRect.xMax - p.Left) / stride)));
            if (firstColumn >= endColumn) return;
            float originY = visibleRect.yMin - p.Top;
            float extentY = visibleRect.yMax - p.Top;

            int startRow = _rowPrefix.IndexAtOffset(originY);
            double cursorY = _rowPrefix.PrefixLength(startRow);

            for (int r = startRow; r < _rows; r++)
            {
                float rowH = _rowHeights[r];
                if ((float)cursorY >= extentY) break;
                if (cursorY + rowH > originY)
                {
                    int first = r * _cols;
                    int last = Mathf.Min(n - first, endColumn);
                    for (int c = firstColumn; c < last; c++)
                    {
                        int i = first + c;
                        float x = (float)(p.Left + c * stride);
                        Rect rect = new Rect(x, p.Top + (float)cursorY, _cellWidth, rowH);
                        writer.Add(i, _templateIds[i], rect, _sizes[i]);
                    }
                }
                cursorY += (double)rowH + _options.Spacing;
            }
        }

        public bool ResizeItem(int visibleIndex, Vector2 realSize)
        {
            if (visibleIndex < 0 || visibleIndex >= Count || !SizePrefixCache.IsValidLength(realSize.y)) return false;
            int row = visibleIndex / _cols;

            realSize.x = _cellWidth;
            if (Mathf.Approximately(_sizes[visibleIndex].y, realSize.y)) return false;
            _sizes[visibleIndex] = realSize;

            if (_batching)
            {
                _dirtyRows.Add(row);
                return false;
            }

            if (!UpdateRowHeight(row)) return false;
            RefreshContentSize();
            return true;
        }

        void IBatchedVirtualLayout.BeginResizeBatch() => _batching = true;

        bool IBatchedVirtualLayout.EndResizeBatch()
        {
            _batching = false;
            bool changed = false;
            foreach (int row in _dirtyRows) changed |= UpdateRowHeight(row);
            _dirtyRows.Clear();
            if (changed) RefreshContentSize();
            return changed;
        }

        private bool UpdateRowHeight(int row)
        {
            // 原来最高的条目也可能缩小，按当前各列高度重算。
            float newRowH = 0f;
            int first = row * _cols;
            int last = first + Mathf.Min(Count - first, _cols);
            for (int i = first; i < last; i++) newRowH = Mathf.Max(newRowH, _sizes[i].y);

            if (Mathf.Approximately(newRowH, _rowHeights[row])) return false;
            _rowHeights[row] = newRowH;
            _rowPrefix.SetLength(row, newRowH);
            return true;
        }

        public Rect GetRect(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= Count) return default;
            int row = visibleIndex / _cols;
            int col = visibleIndex % _cols;
            var p = _options.Padding;
            float x = (float)(p.Left + col * ((double)_cellWidth + _options.Spacing));
            float y = p.Top + _rowPrefix.Prefix(row);
            return new Rect(x, y, _cellWidth, _rowHeights[row]);
        }

        public float GetScrollOffsetForIndex(int visibleIndex)
        {
            int row = visibleIndex / _cols;
            return _options.Padding.Top + _rowPrefix.Prefix(row);
        }

        private void RefreshContentSize()
        {
            var p = _options.Padding;
            ContentSize = new Vector2(_viewport.x, Count == 0 ? 0f : Mathf.Max(0f, p.Top + _rowPrefix.TotalLength + p.Bottom));
        }
    }
}
