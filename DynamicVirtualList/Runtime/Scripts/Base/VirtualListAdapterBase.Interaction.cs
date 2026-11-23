using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    public abstract partial class VirtualListAdapterBase
    {
        private Action<VirtualItemCell, int> _onItemClicked;
        private Action _onRefreshRequested;
        private Action<int> _onListRefreshed;
        private Action<int> _onItemRefreshed;
        private Action<Vector2> _onScrollStarted;
        private Action<Vector2> _onScrollChanged;
        private Action<Vector2> _onScrollEnded;

        private readonly HashSet<int> _pendingItemRefreshes = new HashSet<int>();
        private readonly HashSet<int> _refreshingItems = new HashSet<int>();
        private bool _processingItemRefreshes;
        private bool _listRefreshPending;

        private readonly Queue<(Action<Vector2> Callback, Vector2 Position)> _scrollNotifications =
            new Queue<(Action<Vector2>, Vector2)>();
        private Vector2 _reportedScrollPosition;
        private long _reportedLoopCycle;
        private bool _scrollActive;
        private bool _dispatchingScroll;
        private bool _notifyScrollOnLayout;

        /// <inheritdoc/>
        public Vector2 ScrollPosition => _scrollOrigin;

        public void RegisterItemClickCallback(Action<VirtualItemCell, int> callback) => _onItemClicked = callback;
        public void RegisterRefreshRequestedCallback(Action callback) => _onRefreshRequested = callback;
        public void RegisterListRefreshedCallback(Action<int> callback) => _onListRefreshed = callback;
        public void RegisterItemRefreshedCallback(Action<int> callback) => _onItemRefreshed = callback;
        public void RegisterScrollStartedCallback(Action<Vector2> callback) => _onScrollStarted = callback;
        public void RegisterScrollChangedCallback(Action<Vector2> callback) => _onScrollChanged = callback;
        public void RegisterScrollEndedCallback(Action<Vector2> callback) => _onScrollEnded = callback;

        internal void HandleItemClick(VirtualItemCell holder)
        {
            int index = holder.VisibleIndex;
            if (_updatingView || _dirtyLayout || !IsInitialized || !isActiveAndEnabled
                || !holder.isActiveAndEnabled || index < 0 || index >= _itemCount) return;

            try
            {
                _onItemClicked?.Invoke(holder, index);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <inheritdoc/>
        public void RequestRefresh()
        {
            if (!IsInitialized) return;
            if (_onRefreshRequested == null)
            {
                RefreshItems();
                return;
            }

            try
            {
                _onRefreshRequested();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <inheritdoc/>
        public void RefreshItems() => RefreshItems(_itemCount);

        /// <inheritdoc/>
        public void RefreshItem(int index)
        {
            if (!IsInitialized) throw new InvalidOperationException("Initialize the adapter before refreshing items.");
            if (index < 0 || index >= _itemCount) throw new ArgumentOutOfRangeException(nameof(index));
            _pendingItemRefreshes.Add(index);
            ProcessItemRefreshes();
        }

        private void ProcessItemRefreshes()
        {
            if (_pendingItemRefreshes.Count == 0 || _processingItemRefreshes || _updatingView
                || _dirtyLayout || !IsInitialized || !isActiveAndEnabled) return;

            // 绑定和回收回调可能再次请求刷新，新请求由下一批处理。
            _refreshingItems.Clear();
            foreach (int index in _pendingItemRefreshes) _refreshingItems.Add(index);
            _pendingItemRefreshes.Clear();
            _processingItemRefreshes = true;
            try
            {
                _updatingView = true;
                try
                {
                    ApplyItemRefreshes();
                }
                finally
                {
                    _updatingView = false;
                }

                if (!_dirtyLayout && !_pendingRelayout) PublishScroll(false);
                foreach (int index in _refreshingItems)
                {
                    if (_dirtyLayout || !IsInitialized) break;
                    InvokeCallback(_onItemRefreshed, index);
                }
            }
            finally
            {
                _processingItemRefreshes = false;
            }
        }

        private void ApplyItemRefreshes()
        {
            UpdateRefreshedSizes();
            if (_dirtyLayout) return;

            _releaseBuffer.Clear();
            foreach (var key in _active.Keys)
                if (_refreshingItems.Contains(key.Index)) _releaseBuffer.Add(key);
            for (int i = 0; i < _releaseBuffer.Count && !_dirtyLayout; i++) Release(_releaseBuffer[i]);
            if (!_dirtyLayout) LayoutRefreshedItems();
        }

        private void UpdateRefreshedSizes()
        {
            var anchor = _visibleBuffer.Count > 0 ? _visibleBuffer[0] : null;
            float oldOffset = anchor != null ? _layout.GetScrollOffsetForIndex(anchor.Index) : 0f;
            var estimate = _callbacks.MeasureItem == null ? _callbacks.GetEstimatedSize : null;
            var batch = estimate != null ? _layout as IBatchedVirtualLayout : null;
            batch?.BeginResizeBatch();
            bool resized = false;
            try
            {
                foreach (int index in _refreshingItems)
                {
                    if (index < _measuredItems.Length) _measuredItems[index] = false;
                    if (estimate == null) continue;
                    float size = SizePrefixCache.Estimate(Axis(estimate(index)));
                    if (_dirtyLayout) break;
                    resized |= _layout.ResizeItem(index, AlongAxis(size));
                }
            }
            finally
            {
                if (batch != null) resized |= batch.EndResizeBatch();
            }

            if (resized && !_dirtyLayout) AdjustAfterResize(anchor, oldOffset);
        }

        private void PublishScroll(bool reportMovement, bool dispatch = true)
        {
            Vector2 previous = _reportedScrollPosition;
            bool changed = reportMovement && (!previous.Equals(_scrollOrigin) || _reportedLoopCycle != _loopCycle);
            bool moving = IsInitialized && _processing && !_dirtyLayout && _motion.State != MotionState.Idle;
            _reportedScrollPosition = _scrollOrigin;
            _reportedLoopCycle = _loopCycle;

            if (!_scrollActive && (moving || changed))
            {
                _scrollActive = true;
                QueueScrollCallback(_onScrollStarted, previous);
            }
            if (changed) QueueScrollCallback(_onScrollChanged, _scrollOrigin);
            if (_scrollActive && !moving)
            {
                _scrollActive = false;
                QueueScrollCallback(_onScrollEnded, _scrollOrigin);
            }

            if (dispatch) DispatchScrollCallbacks();
        }

        private void QueueScrollCallback(Action<Vector2> callback, Vector2 position)
        {
            if (callback != null) _scrollNotifications.Enqueue((callback, position));
        }

        private void DispatchScrollCallbacks()
        {
            // 回调内继续滚动时，新通知排在本次通知之后。
            if (_updatingView || _dispatchingScroll || _scrollNotifications.Count == 0) return;
            _dispatchingScroll = true;
            try
            {
                while (_scrollNotifications.Count > 0)
                {
                    var notification = _scrollNotifications.Dequeue();
                    InvokeCallback(notification.Callback, notification.Position);
                }
            }
            finally
            {
                _dispatchingScroll = false;
            }
        }

        private void InvokeCallback<T>(Action<T> callback, T value)
        {
            try
            {
                callback?.Invoke(value);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void ClearInteractionCallbacks()
        {
            _onItemClicked = null;
            _onRefreshRequested = null;
            _onListRefreshed = null;
            _onItemRefreshed = null;
            _onScrollStarted = null;
            _onScrollChanged = null;
            _onScrollEnded = null;
            _listRefreshPending = false;
            _pendingItemRefreshes.Clear();
        }
    }
}
