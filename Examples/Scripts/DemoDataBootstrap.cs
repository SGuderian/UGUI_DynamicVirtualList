using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public sealed class DemoDataBootstrap : MonoBehaviour
    {
        internal const int MinimumRowHeight = 44;

        [SerializeField] private VirtualListAdapterBase _view;
        [SerializeField] private Text _status;
        [SerializeField, Min(0)] private int _itemCount = 100;
        [SerializeField] private bool _randomizeVerticalHeights;
        [SerializeField] private int _heightSeed = 421;
        [SerializeField, Range(0, 4)] private int _treeDepth = 2;
        [SerializeField, Range(1, 4)] private int _childrenPerNode = 3;

        private readonly List<DemoItem> _items = new List<DemoItem>();
        private readonly List<DemoTreeNode> _roots = new List<DemoTreeNode>();
        private readonly List<DemoTreeNode> _visibleNodes = new List<DemoTreeNode>();
        private DemoItem _selectedItem;
        private int _scrollStatusBucket;
        private int _nextId;
        private System.Random _heightRandom;
        private bool _initialized;

        internal bool IsScrolling { get; private set; }
        internal float RowHeight(int index) => _items[index].LayoutHeight;
        internal float MaximumRowHeight(int index) => Mathf.Max(RowHeight(index), _items[index].EstimatedHeight);

        internal float MinimumContentLength()
        {
            float length = 0f;
            foreach (var item in _items)
                length += Mathf.Min(item.EstimatedHeight, item.LayoutHeight);
            return length;
        }

        private bool IsTree => _view.LayoutMode == VirtualLayoutMode.Tree;
        private int Count => IsTree ? _visibleNodes.Count : _items.Count;

        private void OnEnable()
        {
            if (_view == null) return;
            if (!_initialized)
            {
                _heightRandom = new System.Random(_heightSeed);
                Append(Mathf.Max(0, _itemCount));
                _initialized = true;
            }

            IVirtualListAdapter adapter = _view;
            adapter.RegisterCallbacks(BindItem,
                getEstimatedSize: EstimateItem, measureItem: MeasureItem,
                getDepth: IsTree ? GetDepth : (Func<int, int>)null);
            adapter.RegisterItemClickCallback(OnItemClicked);
            adapter.RegisterRefreshRequestedCallback(AppendAndRefresh);
            adapter.RegisterListRefreshedCallback(OnListRefreshed);
            adapter.RegisterItemRefreshedCallback(OnItemRefreshed);
            adapter.RegisterScrollStartedCallback(OnScrollStarted);
            adapter.RegisterScrollChangedCallback(OnScrollChanged);
            adapter.RegisterScrollEndedCallback(OnScrollEnded);
            adapter.Init(Count);
            ShowStatus(IsTree ? "Click a branch to expand or collapse it." : "Drag to scroll or click an item.");
        }

        private void OnDisable()
        {
            if (_view != null) _view.ClearCallbacks();
            IsScrolling = false;
        }

        private void BindItem(VirtualItemCell holder, int index)
        {
            if (IsTree)
            {
                var node = _visibleNodes[index];
                ((DemoTreeCell)holder).Show(node.Label, node.Children.Count > 0, node.Expanded);
                return;
            }
            var item = _items[index];
            ((DemoCell)holder).Show(item == _selectedItem ? item.Title + "  [selected]" : item.Title, item.Body, item.LayoutHeight);
        }

        private Vector2 EstimateItem(int index) => IsTree
            ? new Vector2(0f, 48f) : new Vector2(180f, _items[index].EstimatedHeight);

        private static Vector2 MeasureItem(VirtualItemCell holder, int index) => holder.GetPreferredSize();

        private void OnItemClicked(VirtualItemCell holder, int index)
        {
            if (IsTree)
            {
                ToggleNode(index);
                return;
            }

            var item = _items[index];
            int previous = _selectedItem != null ? _items.IndexOf(_selectedItem) : -1;
            _selectedItem = item == _selectedItem ? null : item;
            if (previous >= 0 && previous != index) _view.RefreshItem(previous);
            _view.RefreshItem(index);
        }

        private int GetDepth(int index) => _visibleNodes[index].Depth;

        public void AppendRows() => _view.RequestRefresh();

        private void AppendAndRefresh()
        {
            Append(30);
            _view.RefreshItems(Count);
        }

        private void OnListRefreshed(int count) => ShowStatus($"List refreshed. {count} rows.");

        private void OnItemRefreshed(int index) => ShowStatus($"Item {index + 1} refreshed.");

        private void OnScrollStarted(Vector2 position)
        {
            IsScrolling = true;
            _scrollStatusBucket = Mathf.FloorToInt(ScrollOffset(position) / 32f);
            ShowStatus("Scrolling started.");
        }

        private void OnScrollChanged(Vector2 position)
        {
            float offset = ScrollOffset(position);
            int bucket = Mathf.FloorToInt(offset / 32f);
            if (_scrollStatusBucket == bucket) return;
            _scrollStatusBucket = bucket;
            ShowStatus($"Scrolling at {offset:F0}.");
        }

        private void OnScrollEnded(Vector2 position)
        {
            IsScrolling = false;
            ShowStatus($"Scrolling ended at {ScrollOffset(position):F0}.");
        }

        private float ScrollOffset(Vector2 position)
            => _view.LayoutMode == VirtualLayoutMode.HorizontalList ? position.x : position.y;

        private void ShowStatus(string message)
        {
            if (_status != null) _status.text = message;
        }

        public void ReverseOrder()
        {
            int index = -1;
            float offset = 0f;
            _view.TryGetViewportAnchor(out index, out offset);
            if (IsTree)
            {
                var anchor = index >= 0 ? _visibleNodes[index] : null;
                _roots.Reverse();
                RebuildVisibleNodes();
                index = anchor != null ? _visibleNodes.IndexOf(anchor) : -1;
            }
            else
            {
                _items.Reverse();
                if (index >= 0) index = _items.Count - 1 - index;
            }
            _view.RefreshItems(Count, index, offset);
        }

        public void JumpToLast()
        {
            if (Count > 0) _view.ScrollToIndex(Count - 1);
        }

        public void ToggleLoop() => _view.SetLoopScrolling(!_view.LoopScrolling);

        private void ToggleNode(int index)
        {
            var node = _visibleNodes[index];
            if (node.Children.Count == 0) return;
            _view.TryGetViewportAnchor(out int anchorIndex, out float offset);
            var anchor = anchorIndex >= 0 ? _visibleNodes[anchorIndex] : node;
            node.Expanded = !node.Expanded;
            RebuildVisibleNodes();
            int newIndex = _visibleNodes.IndexOf(anchor);
            if (newIndex < 0)
            {
                newIndex = _visibleNodes.IndexOf(node);
                offset = 0f;
            }
            _view.RefreshItems(Count, newIndex, offset);
        }

        private void Append(int count)
        {
            if (IsTree)
            {
                while (count > 0) _roots.Add(BuildNode($"Node {++_nextId}", 0, ref count));
                RebuildVisibleNodes();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int id = ++_nextId;
                if (_randomizeVerticalHeights && _view.LayoutMode == VirtualLayoutMode.VerticalList)
                {
                    int band = _heightRandom.Next(10);
                    int height = band < 4 ? _heightRandom.Next(MinimumRowHeight, 69) :
                        band < 7 ? _heightRandom.Next(82, 145) : _heightRandom.Next(158, 281);
                    float estimate = band < 4 ? 58f : band < 7 ? 108f : 182f;
                    _items.Add(new DemoItem($"Item {id}", $"Height {height}  /  estimate {estimate:F0}", estimate, height));
                    continue;
                }

                bool multiline = id % 3 == 0;
                string body = multiline
                    ? "This row has a longer description. Its preferred height is measured after layout and changes with the viewport width."
                    : "A short description.";
                _items.Add(new DemoItem($"Item {id}", body, multiline ? 84f : 44f));
            }
        }

        private DemoTreeNode BuildNode(string label, int depth, ref int remaining)
        {
            bool canHaveChildren = depth < _treeDepth;
            var node = new DemoTreeNode(label, depth, canHaveChildren && depth == 0);
            remaining--;
            for (int i = 0; canHaveChildren && i < _childrenPerNode && remaining > 0; i++)
                node.Children.Add(BuildNode($"{label}.{i + 1}", depth + 1, ref remaining));
            return node;
        }

        private void RebuildVisibleNodes()
        {
            _visibleNodes.Clear();
            foreach (var root in _roots) AddVisibleNode(root);
        }

        private void AddVisibleNode(DemoTreeNode node)
        {
            _visibleNodes.Add(node);
            if (!node.Expanded) return;
            foreach (var child in node.Children) AddVisibleNode(child);
        }
    }
}
