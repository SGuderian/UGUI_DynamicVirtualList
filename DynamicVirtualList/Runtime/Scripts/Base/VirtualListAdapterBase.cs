using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VirtualList
{
    [RequireComponent(typeof(RectTransform))]
    public abstract partial class VirtualListAdapterBase : MonoBehaviour, IVirtualListAdapter,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("References")]
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Scrollbar _verticalScrollbar;
        [SerializeField] private Scrollbar _horizontalScrollbar;
        [SerializeField] private List<VirtualItemTemplate> _itemTemplates = new List<VirtualItemTemplate>();

        [Header("Layout")]
        [SerializeField, Min(0f)] private float _spacing = 4f;
        [SerializeField] private RectOffset _padding;
        [Tooltip("首尾连接，允许双向循环滑动。")]
        [SerializeField] private bool _loopScrolling;
        [Tooltip("按显示顺序排列在屏条目的层级索引。")]
        [SerializeField] private bool _orderVisibleNodes = true;

        [Header("Scroll Motion")]
        [SerializeField] private ScrollSettings _scrollSettings = ScrollSettings.Default;

        [Header("Delayed Measure")]
        [Tooltip("条目入屏后等待的帧数，待 UGUI 布局刷新完成再读取真实尺寸回填缓存。")]
        [SerializeField] private int _measureDelayFrames = 1;
        [Tooltip("默认刷新时先完成在屏条目的布局和测量，滚动中入屏的条目在画布布局后测量。关闭后使用下一帧测量。")]
        [SerializeField] private bool _measureAfterCanvasRefresh = true;

        private const int MaxRefreshMeasurePasses = 4;
        private static bool _synchronizingCanvases;

        private IVirtualLayout _layout;
        private LayoutOptions _layoutOptions;
        private ItemPool _pool;
        private Dictionary<int, GameObject> _configuredPrefabs;
        private bool _checkInitialTemplateIds;
        private AdapterCallbacks _registeredCallbacks;
        private AdapterCallbacks _callbacks;
        private int _itemCount;
        private ScrollMotion _motion;

        // 循环时同一显示索引可能同时出现多次，按轮次区分实例。
        private readonly Dictionary<(long Cycle, int Index), ActiveCell> _active = new Dictionary<(long, int), ActiveCell>();
        private readonly VisibleItemBuffer _visibleBuffer = new VisibleItemBuffer();
        private readonly List<VirtualItemData> _customLayoutBuffer = new List<VirtualItemData>();
        private readonly List<(long Cycle, int Index)> _visibleKeys = new List<(long, int)>();
        private readonly HashSet<(long Cycle, int Index)> _seenThisFrame = new HashSet<(long, int)>();
        private readonly HashSet<int> _invalidTemplates = new HashSet<int>();
        private readonly List<(long Cycle, int Index)> _releaseBuffer = new List<(long, int)>();

        // 每个在屏实例记录最早可测量的帧。
        private readonly Dictionary<(long Cycle, int Index), int> _pendingMeasure = new Dictionary<(long, int), int>();
        private readonly List<(long Cycle, int Index)> _measureBuffer = new List<(long, int)>();
        private readonly HashSet<int> _measuredThisPass = new HashSet<int>();
        private readonly BitArray _measuredItems = new BitArray(0);
        private readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();
        private Coroutine _measureRoutine;
        private SnapResolver _snapResolver;

        // 逻辑坐标以内容左上角为原点，y 向下。
        private Vector2 _scrollOrigin;
        private Vector2 _viewportSize;
        private Vector3 _placementOrigin;
        private Vector3 _placementRight = Vector3.right;
        private Vector3 _placementUp = Vector3.up;
        private Quaternion _placementRotation = Quaternion.identity;
        private Vector3 _placementScale = Vector3.one;
        private float _loopLength;
        private long _loopCycle;
        private bool _dirtyLayout = true;
        private bool _processing;
        private bool _updatingView;
        private bool _pendingRelayout;
        private int _anchorIndex = -1;
        private float _anchorOffset;
        private bool _siblingsOrdered;
        private VirtualLayoutMode _builtMode;
        private int _targetIndex = -1;
        private long _targetCycle;
        private SnapAlign? _targetAlignment;

        private float _lastDragTime;
        private Vector2 _lastDragPosition;
        private int _dragPointerId;

        private struct ActiveCell
        {
            public GameObject Instance;
            public VirtualItemCell Cell;
            public RectTransform Transform;
            public int TemplateId;
        }

        private struct AdapterCallbacks
        {
            public Action<VirtualItemCell, int> BindItem;
            public Func<int, int> GetTemplateId;
            public Func<int, Vector2> GetEstimatedSize;
            public Func<VirtualItemCell, int, Vector2> MeasureItem;
            public Action<VirtualItemCell, int> RecycleItem;
            public Func<int, int> GetDepth;
        }

        private void PrepareAdapter(int itemCount)
        {
            var viewport = ValidateViewportAndContent();
            var prefabs = BuildValidatedPrefabMap(viewport);
            ValidateLayoutConfiguration(BuildLayoutOptions());
            if (itemCount > 0 && _registeredCallbacks.GetTemplateId == null)
                ValidateTemplateId(prefabs, 0, 0);
            if (_pool == null) BuildLayout();

            _viewport = viewport;
            if (_content == null)
            {
                var go = new GameObject("Content", typeof(RectTransform));
                go.transform.SetParent(_viewport, false);
                _content = (RectTransform)go.transform;
                _content.anchorMin = Vector2.zero;
                _content.anchorMax = Vector2.one;
                _content.offsetMin = _content.offsetMax = Vector2.zero;
            }

            _configuredPrefabs = prefabs;
            if (_pool != null) return;
            _pool = new ItemPool(_content, prefabs);
            _motion = new ScrollMotion(_scrollSettings);
            _snapResolver = ResolveAndTrackSnap;
        }

        protected virtual void OnEnable()
        {
            if (IsInitialized) StartProcessing();
        }

        private void StartProcessing()
        {
            if (_processing) return;
            _processing = true;
            if (_verticalScrollbar != null) _verticalScrollbar.onValueChanged.AddListener(OnVerticalScrollbarChanged);
            if (_horizontalScrollbar != null) _horizontalScrollbar.onValueChanged.AddListener(OnHorizontalScrollbarChanged);
            _measureRoutine = StartCoroutine(MeasureRoutine());
            try
            {
                RefreshViewport(forceLayout: true);
            }
            catch
            {
                ClearCallbacks();
                throw;
            }
        }

        protected virtual void OnDisable()
        {
            StopProcessing();
            PublishScroll(false);
        }

        private void StopProcessing()
        {
            if (!_processing) return;
            _processing = false;
            if (_verticalScrollbar != null) _verticalScrollbar.onValueChanged.RemoveListener(OnVerticalScrollbarChanged);
            if (_horizontalScrollbar != null) _horizontalScrollbar.onValueChanged.RemoveListener(OnHorizontalScrollbarChanged);
            _motion?.CancelMotion();
            if (_measureRoutine != null)
            {
                StopCoroutine(_measureRoutine);
                _measureRoutine = null;
            }
        }

        protected virtual void OnDestroy()
        {
            IsInitialized = false;
            if (_pool == null) return;
            _updatingView = true;
            try
            {
                RecycleAll();
            }
            finally
            {
                _pool.Clear();
                _registeredCallbacks = _callbacks = default;
                ClearInteractionCallbacks();
            }
        }

        private void BuildLayout()
        {
            _layout = CreateLayout() ?? throw ConfigurationError("CreateLayout() returned null.");
            _builtMode = LayoutMode;
        }

        protected abstract IVirtualLayout CreateLayout();

        protected virtual void ConfigureLayout(ref LayoutOptions options) { }

        /// <summary>提供代码配置的条目模板。同一个 ID 优先使用 Inspector 中的模板。</summary>
        protected virtual Dictionary<int, GameObject> GetPrefabMap() => null;

        private LayoutOptions BuildLayoutOptions()
        {
            var options = LayoutOptions.Default;
            options.Mode = LayoutMode;
            options.Spacing = _spacing;
            options.Padding = _padding;
            ConfigureLayout(ref options);
            return options;
        }

        public int ItemCount => _itemCount;
        public bool IsInitialized { get; private set; }
        public abstract VirtualLayoutMode LayoutMode { get; }

        /// <inheritdoc/>
        public void RegisterCallbacks(Action<VirtualItemCell, int> bindItem,
            Func<int, int> getTemplateId = null, Func<int, Vector2> getEstimatedSize = null,
            Func<VirtualItemCell, int, Vector2> measureItem = null,
            Action<VirtualItemCell, int> recycleItem = null, Func<int, int> getDepth = null)
        {
            _registeredCallbacks = new AdapterCallbacks
            {
                BindItem = bindItem ?? throw new ArgumentNullException(nameof(bindItem)),
                GetTemplateId = getTemplateId,
                GetEstimatedSize = getEstimatedSize,
                MeasureItem = measureItem,
                RecycleItem = recycleItem,
                GetDepth = getDepth
            };
        }

        /// <inheritdoc/>
        public void Init(int itemCount = 0)
        {
            ValidateItemCount(itemCount);
            if (IsInitialized) return;
            if (_registeredCallbacks.BindItem == null)
                throw ConfigurationError("Register a BindItem callback before calling Init().");
            PrepareAdapter(itemCount);
            _itemCount = itemCount;
            _dirtyLayout = true;
            _checkInitialTemplateIds = true;
            IsInitialized = true;
            if (isActiveAndEnabled) StartProcessing();
        }

        /// <inheritdoc/>
        public void RefreshItems(int itemCount, int anchorIndex = -1, float offsetWithinAnchor = 0f)
        {
            ValidateItemCount(itemCount);
            if (!IsInitialized) throw new InvalidOperationException("Initialize the adapter before refreshing items.");
            if (anchorIndex < -1 || anchorIndex >= itemCount) throw new ArgumentOutOfRangeException(nameof(anchorIndex));
            _itemCount = itemCount;
            _anchorIndex = anchorIndex;
            _anchorOffset = FiniteOrZero(offsetWithinAnchor);
            _listRefreshPending = true;
            _pendingItemRefreshes.Clear();
            Rebind();
        }

        /// <inheritdoc/>
        public void ClearCallbacks()
        {
            IsInitialized = false;
            _registeredCallbacks = default;
            _itemCount = 0;
            _anchorIndex = -1;
            _checkInitialTemplateIds = false;
            StopProcessing();
            PublishScroll(false, dispatch: false);
            ClearInteractionCallbacks();
            Rebind();
            DispatchScrollCallbacks();
        }

        /// <inheritdoc/>
        public bool TryGetViewportAnchor(out int index, out float offsetWithinItem)
        {
            index = -1;
            offsetWithinItem = 0f;
            if (!IsInitialized || _dirtyLayout || _visibleBuffer.Count == 0) return false;
            var first = _visibleBuffer[0];
            index = first.Index;
            offsetWithinItem = Axis(_scrollOrigin - first.Rect.position);
            return true;
        }

        private static void ValidateItemCount(int itemCount)
        {
            if (itemCount < 0)
                throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "Item count must be non-negative.");
        }

        public bool LoopScrolling => _loopScrolling;

        public void SetLoopScrolling(bool enabled)
        {
            if (_loopScrolling == enabled) return;
            _loopScrolling = enabled;
            Rebind();
        }

        /// <summary>使用现有条目数和配置重建布局。业务数据变化时调用 RefreshItems。</summary>
        public void Rebind() => Rebind(synchronizeViewport: true);

        private void Rebind(bool synchronizeViewport)
        {
            _dirtyLayout = true;
            if (_pool == null || _updatingView || (!isActiveAndEnabled && IsInitialized)) return;
            _updatingView = true;
            try
            {
                Rebuild(synchronizeViewport);
            }
            catch
            {
                _dirtyLayout = true;
                throw;
            }
            finally
            {
                _updatingView = false;
            }
            if (!_dirtyLayout)
            {
                bool refreshed = _listRefreshPending && IsInitialized;
                _listRefreshPending = false;
                int count = _itemCount;
                var callback = _onListRefreshed;
                PublishScroll(false);
                if (refreshed) InvokeCallback(callback, count);
            }
        }

        private void Rebuild(bool synchronizeViewport)
        {
            _dirtyLayout = false;
            _notifyScrollOnLayout = false;
            _motion.CancelMotion();
            _targetIndex = -1;

            RecycleAll();
            _visibleBuffer.Clear();
            _visibleKeys.Clear();
            _siblingsOrdered = false;
            if (_dirtyLayout) return;
            if (synchronizeViewport && IsInitialized) SynchronizeViewport();
            if (_dirtyLayout) return;
            _viewportSize = _viewport.rect.size;
            _loopCycle = 0;
            _loopLength = 0f;
            _motion.Looping = false;
            _invalidTemplates.Clear();
            if (_builtMode != LayoutMode) BuildLayout();
            _callbacks = IsInitialized ? _registeredCallbacks : default;
            int anchorIndex = _anchorIndex;
            float anchorOffset = _anchorOffset;
            _anchorIndex = -1;
            if (_callbacks.BindItem == null)
            {
                if (_layout.Count != 0) BuildLayout();
                _measuredItems.Length = 0;
                _scrollOrigin = Vector2.zero;
                _pendingRelayout = false;
                RefreshScrollbars(Vector2.zero);
                return;
            }

            if (!_pool.MatchesConfiguration(_content, _configuredPrefabs))
            {
                _pool.Clear();
                _pool = new ItemPool(_content, _configuredPrefabs);
            }

            _layoutOptions = BuildLayoutOptions();
            var templateId = _callbacks.GetTemplateId;
            if (_checkInitialTemplateIds && templateId != null) templateId = ResolveInitialTemplateId;
            if (_checkInitialTemplateIds && templateId == null && _itemCount > 0)
                ValidateTemplateId(_configuredPrefabs, 0, 0);
            _layout.Rebuild(_itemCount, _layoutOptions, _viewportSize,
                _callbacks.GetEstimatedSize, templateId, _callbacks.GetDepth);
            if (!_dirtyLayout) _checkInitialTemplateIds = false;
            _measuredItems.Length = _layout.Count;
            _measuredItems.SetAll(false);
            if (anchorIndex >= 0 && anchorIndex < _layout.Count)
                _scrollOrigin = AlongAxis(_layout.GetScrollOffsetForIndex(anchorIndex) + anchorOffset);
            if (!_dirtyLayout && isActiveAndEnabled) LayoutRefreshedItems();
        }

        private void SynchronizeViewport()
        {
            // 先应用父级布局。刷新可能由 Canvas 回调触发，避免嵌套重建。
            if (!_synchronizingCanvases && !IsCanvasRebuilding() && _viewport.GetComponentInParent<Canvas>() != null)
            {
                _synchronizingCanvases = true;
                try
                {
                    Canvas.ForceUpdateCanvases();
                }
                finally
                {
                    _synchronizingCanvases = false;
                }
            }

            _viewport.ForceUpdateRectTransforms();
            _content.ForceUpdateRectTransforms();
        }

        private static bool IsCanvasRebuilding()
            => CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics();

        private void RecycleAll()
        {
            _releaseBuffer.Clear();
            foreach (var key in _active.Keys) _releaseBuffer.Add(key);
            for (int i = 0; i < _releaseBuffer.Count; i++) Release(_releaseBuffer[i]);
            _pendingMeasure.Clear();
        }

        /// <inheritdoc/>
        public void ScrollToIndex(int visibleIndex)
        {
            if (_dirtyLayout || _callbacks.BindItem == null || _layout == null || visibleIndex < 0 || visibleIndex >= _layout.Count) return;
            float offset = _layout.GetScrollOffsetForIndex(visibleIndex);
            float turns = _loopLength > 0f ? Mathf.Round((Axis(_scrollOrigin) - offset) / _loopLength) : 0f;
            _targetIndex = visibleIndex;
            _targetCycle = _loopCycle + (long)turns;
            _targetAlignment = null;
            offset += turns * _loopLength;
            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _motion.ScrollTo(offset, _scrollSettings.SmoothScroll,
                _scrollSettings.SmoothDuration, _scrollSettings.SmoothCurve);
            _scrollOrigin = _motion.Origin;
            Relayout(notifyScroll: true);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_dirtyLayout || _callbacks.BindItem == null || _layout.Count == 0 || !isActiveAndEnabled || _motion.State == MotionState.Dragging
                || eventData.button != PointerEventData.InputButton.Left
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position,
                    eventData.pressEventCamera, out _lastDragPosition)) return;
            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _motion.BeginDrag();
            _dragPointerId = eventData.pointerId;
            _lastDragTime = Time.unscaledTime;
            PublishScroll(false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dirtyLayout || _motion?.State != MotionState.Dragging || eventData.pointerId != _dragPointerId
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position,
                    eventData.pressEventCamera, out var position)) return;
            Vector2 delta = position - _lastDragPosition;
            _lastDragPosition = position;
            Vector2 mapped = new Vector2(-delta.x, delta.y);

            float dt = Mathf.Max(Time.unscaledDeltaTime, Time.unscaledTime - _lastDragTime);
            _lastDragTime = Time.unscaledTime;

            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _scrollOrigin = _motion.Drag(mapped, dt);
            Relayout(notifyScroll: true);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_motion?.State != MotionState.Dragging || eventData.pointerId != _dragPointerId) return;
            // 拖拽期间的延迟测量可能改变边界，松手时使用当前内容尺寸。
            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _motion.EndDrag(Time.unscaledTime - _lastDragTime < 0.1f);
            if (_motion.State == MotionState.Idle) Relayout(notifyScroll: true);
        }

        protected virtual void Update()
        {
            if (!IsInitialized)
            {
                if (_dirtyLayout && _callbacks.BindItem != null) Rebind();
                return;
            }
            RefreshViewport(forceLayout: _pendingRelayout);
            if (_dirtyLayout || _motion == null || _layout == null || _callbacks.BindItem == null) return;
            ProcessItemRefreshes();
            if (_dirtyLayout || !IsInitialized) return;
            if (!_motion.IsAnimating) return;

            Vector2 previous = _scrollOrigin;
            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _scrollOrigin = _motion.Update(Time.unscaledDeltaTime, _snapResolver, out _);
            if (_scrollOrigin != previous || !_motion.IsAnimating) Relayout(notifyScroll: true);
        }

        private void RefreshViewport(bool forceLayout)
        {
            Vector2 size = _viewport.rect.size;
            if (_dirtyLayout)
            {
                Rebind();
                return;
            }

            if (_viewportSize != size)
            {
                bool crossAxisChanged = _layout.IsVertical ? _viewportSize.x != size.x : _viewportSize.y != size.y;
                if (_callbacks.BindItem != null && (crossAxisChanged || !(_layout is IBufferedVirtualLayout)))
                {
                    if (_anchorIndex < 0) TryGetViewportAnchor(out _anchorIndex, out _anchorOffset);
                    Rebind(synchronizeViewport: false);
                    return;
                }
                _viewportSize = size;
                RetargetAnimation();
                forceLayout = true;
            }
            if (forceLayout || (!_motion.IsAnimating && _active.Count > 0 && UpdatePlacement(out bool moved) && moved))
                Relayout();
        }

        private float ResolveAndTrackSnap(float offset, SnapAlign align, out int index)
        {
            float target = ResolveSnap(offset, align, out index);
            _targetIndex = index;
            _targetAlignment = align;
            _targetCycle = _loopCycle;
            if (index >= 0 && _loopLength > 0f)
                _targetCycle += (long)Mathf.Round((target - SnapOffset(index, align)) / _loopLength);
            return target;
        }

        private float ResolveSnap(float currentOffset, SnapAlign align, out int itemIndex)
        {
            itemIndex = -1;
            int count = _layout.Count;
            if (count == 0) return currentOffset;

            float firstTarget = SnapOffset(0, align);
            float cycleOffset = _loopLength > 0f
                ? Mathf.Floor((currentOffset - firstTarget) / _loopLength) * _loopLength : 0f;
            float localOffset = currentOffset - cycleOffset;
            int lower = 0;
            int upper = count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (SnapOffset(middle, align) < localOffset) lower = middle + 1;
                else upper = middle;
            }

            int previous = Mathf.Max(0, lower - 1);
            float previousTarget = SnapOffset(previous, align);
            if (lower < count || _loopLength > 0f)
            {
                int next = lower < count ? lower : 0;
                float nextTarget = lower < count ? SnapOffset(next, align) : firstTarget + _loopLength;
                if (Mathf.Abs(nextTarget - localOffset) < Mathf.Abs(previousTarget - localOffset))
                {
                    itemIndex = next;
                    return nextTarget + cycleOffset;
                }
            }
            itemIndex = previous;
            return previousTarget + cycleOffset;
        }

        private float SnapOffset(int index, SnapAlign align)
        {
            Rect rect = _layout.GetRect(index);
            return align == SnapAlign.Center
                ? Axis(rect.center) - Axis(_viewportSize) * 0.5f : Axis(rect.position);
        }

        private void Relayout(bool notifyScroll = false)
        {
            if (_callbacks.BindItem == null && !_updatingView) return;
            _pendingRelayout = true;
            _notifyScrollOnLayout |= notifyScroll;
            if (_updatingView || _dirtyLayout || _callbacks.BindItem == null || _layout == null || !isActiveAndEnabled) return;
            bool reportMovement = _notifyScrollOnLayout;
            _notifyScrollOnLayout = false;
            _updatingView = true;
            try
            {
                LayoutVisible();
            }
            finally
            {
                _updatingView = false;
            }
            if (!_dirtyLayout && !_pendingRelayout) PublishScroll(reportMovement);
            else if (!_dirtyLayout) _notifyScrollOnLayout |= reportMovement;
        }

        private void LayoutVisible()
        {
            if (!UpdatePlacement(out _))
            {
                _pendingRelayout = true;
                return;
            }

            _pendingRelayout = false;
            ConstrainScroll();

            Rect viewportRect = new Rect(_scrollOrigin.x, _scrollOrigin.y, _viewportSize.x, _viewportSize.y);
            FillVisible(viewportRect);

            bool changed = UpdateVisibleKeys();
            if (changed)
            {
                _releaseBuffer.Clear();
                foreach (var cell in _active)
                    if (!_seenThisFrame.Contains(cell.Key)) _releaseBuffer.Add(cell.Key);
                for (int i = 0; i < _releaseBuffer.Count && !_dirtyLayout; i++) Release(_releaseBuffer[i]);
            }

            bool spawned = false;
            bool firstSpawned = false;
            for (int i = 0; i < _visibleBuffer.Count && !_dirtyLayout; i++)
            {
                if (!UpdateOrSpawn(_visibleBuffer[i])) continue;
                spawned = true;
                if (i == 0) firstSpawned = true;
            }
            if (_dirtyLayout) return;
            if (_orderVisibleNodes && (!_siblingsOrdered || changed || spawned)) SyncSiblingOrder(firstSpawned);
            _siblingsOrdered = _orderVisibleNodes;

            RefreshScrollbars(_layout.ContentSize);
        }

        private bool UpdatePlacement(out bool changed)
        {
            Rect viewport = _viewport.rect;
            Rect content = _content.rect;
            Matrix4x4 toContent = _content.worldToLocalMatrix * _viewport.localToWorldMatrix;
            Vector3 right = toContent.MultiplyVector(Vector3.right);
            Vector3 up = toContent.MultiplyVector(Vector3.up);
            float widthScale = right.magnitude;
            float heightScale = up.magnitude;
            changed = false;
            if (!SizePrefixCache.IsValidLength(widthScale) || !SizePrefixCache.IsValidLength(heightScale)) return false;

            Vector3 origin = toContent.MultiplyPoint3x4(new Vector3(viewport.xMin, viewport.yMax))
                - new Vector3(content.xMin, content.yMax);
            if (origin == _placementOrigin && right == _placementRight && up == _placementUp) return true;

            Vector3 horizontal = right / widthScale;
            Vector3 vertical = up / heightScale;
            // 单个 RectTransform 可以补偿平移、旋转和缩放，但无法表示斜切。
            if (Mathf.Abs(Vector3.Dot(horizontal, vertical)) > 0.0001f)
                throw ConfigurationError("Content relative to Viewport produces shear. Keep its axes orthogonal.");

            Vector3 forward = Vector3.Cross(horizontal, vertical);
            if (Vector3.Dot(forward, toContent.MultiplyVector(Vector3.forward)) < 0f)
            {
                forward = -forward;
                widthScale = -widthScale;
            }
            _placementOrigin = origin;
            _placementRight = right;
            _placementUp = up;
            _placementRotation = Quaternion.LookRotation(forward, vertical);
            _placementScale = new Vector3(widthScale, heightScale, 1f);
            changed = true;
            return true;
        }

        private void LayoutRefreshedItems()
        {
            LayoutVisible();
            if (!_measureAfterCanvasRefresh || _measureDelayFrames > 1 || IsCanvasRebuilding()) return;

            // 尺寸缩小可能露出新的条目。超过同步测量轮数的条目留到延迟队列。
            for (int pass = 0; pass < MaxRefreshMeasurePasses && !_dirtyLayout && !_pendingRelayout
                && _pendingMeasure.Count > 0; pass++)
            {
                if (!MeasureVisible(prepareLayout: true)) break;
            }
        }

        private bool UpdateVisibleKeys()
        {
            bool changed = _visibleKeys.Count != _visibleBuffer.Count;
            for (int i = 0; !changed && i < _visibleBuffer.Count; i++)
                changed = _visibleKeys[i] != (_visibleBuffer[i].Cycle, _visibleBuffer[i].Index);
            if (!changed) return false;

            _visibleKeys.Clear();
            _seenThisFrame.Clear();
            for (int i = 0; i < _visibleBuffer.Count; i++)
            {
                var key = (_visibleBuffer[i].Cycle, _visibleBuffer[i].Index);
                _visibleKeys.Add(key);
                _seenThisFrame.Add(key);
            }
            return true;
        }

        private void FillVisible(Rect viewportRect)
        {
            _visibleBuffer.Clear();
            if (viewportRect.width <= 0f || viewportRect.height <= 0f) return;

            if (_loopLength <= 0f)
            {
                AppendVisible(viewportRect, Vector2.zero, 0);
                return;
            }

            float viewportEnd = _layout.IsVertical ? viewportRect.yMax : viewportRect.xMax;
            for (int cycleIndex = 0; cycleIndex * _loopLength < viewportEnd; cycleIndex++)
            {
                Vector2 shift = AlongAxis(cycleIndex * _loopLength);
                var slice = new Rect(viewportRect.position - shift, viewportRect.size);
                AppendVisible(slice, shift, _loopCycle + cycleIndex);
            }
        }

        private void AppendVisible(Rect viewport, Vector2 shift, long cycle)
        {
            var writer = new VisibleItemWriter(_visibleBuffer, shift, cycle);
            if (_layout is IBufferedVirtualLayout buffered)
            {
                buffered.FillVisible(viewport, writer);
                return;
            }

            _layout.FillVisible(viewport, _customLayoutBuffer);
            foreach (var item in _customLayoutBuffer)
                writer.Add(item.Index, item.TemplateId, item.Rect, item.Size, item.Depth);
            _customLayoutBuffer.Clear();
        }

        private void SyncSiblingOrder(bool reverse)
        {
            if (_visibleKeys.Count < 2) return;

            int index = reverse ? _visibleKeys.Count - 1 : 0;
            int step = reverse ? -1 : 1;
            RectTransform previous = null;
            for (; index >= 0 && index < _visibleKeys.Count; index += step)
            {
                if (!_active.TryGetValue(_visibleKeys[index], out var cell) || cell.Transform == null) continue;
                if (previous != null)
                {
                    int sibling = cell.Transform.GetSiblingIndex();
                    int previousSibling = previous.GetSiblingIndex();
                    if (reverse ? sibling > previousSibling : sibling < previousSibling)
                        cell.Transform.SetSiblingIndex(previousSibling);
                }
                previous = cell.Transform;
            }
        }

        private bool UpdateOrSpawn(VirtualItemData data)
        {
            var key = (data.Cycle, data.Index);
            if (_active.TryGetValue(key, out var active))
            {
                if (active.Instance != null && active.Cell != null && active.TemplateId == data.TemplateId)
                {
                    Place(active, data);
                    return false;
                }
                Release(key);
            }
            return !_dirtyLayout && Spawn(data);
        }

        private bool Spawn(VirtualItemData data)
        {
            if (_invalidTemplates.Contains(data.TemplateId)) return false;
            var go = _pool.Acquire(data.TemplateId);
            if (_dirtyLayout)
            {
                _pool.Release(go, data.TemplateId);
                return false;
            }
            var cell = go != null ? go.GetComponent<VirtualItemCell>() : null;
            if (cell == null || !(go.transform is RectTransform rectTransform))
            {
                if (go != null)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
                _invalidTemplates.Add(data.TemplateId);
                Debug.LogError($"Template {data.TemplateId} needs a prefab with a RectTransform and a compatible VirtualItemCell.", this);
                return false;
            }

            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = rectTransform.anchorMin;
            var active = new ActiveCell { Instance = go, Cell = cell, Transform = rectTransform, TemplateId = data.TemplateId };
            try
            {
                // 绑定回调可能读单元格宽高，例如按宽度设置文本换行。
                Place(active, data);
                cell.Assign(this, data.Index, data.TemplateId);
                _callbacks.BindItem(cell, data.Index);
            }
            catch
            {
                Recycle(active);
                throw;
            }
            if (_dirtyLayout)
            {
                Recycle(active);
                return false;
            }
            var key = (data.Cycle, data.Index);
            _active[key] = active;
            Place(active, data);
            if (!HasMeasured(data.Index)) EnqueueMeasure(key);
            return true;
        }

        private void Place(ActiveCell active, VirtualItemData data)
        {
            var rt = active.Transform;
            Vector2 position = data.Rect.position - _scrollOrigin;
            Vector3 anchored = _placementOrigin + _placementRight * position.x - _placementUp * position.y;
            if (!rt.anchoredPosition3D.Equals(anchored)) rt.anchoredPosition3D = anchored;
            if (!rt.localRotation.Equals(_placementRotation)) rt.localRotation = _placementRotation;
            if (!rt.localScale.Equals(_placementScale)) rt.localScale = _placementScale;
            if (!rt.sizeDelta.Equals(data.Rect.size)) rt.sizeDelta = data.Rect.size;
        }

        private void EnqueueMeasure((long Cycle, int Index) key)
        {
            if (_callbacks.MeasureItem == null) return;
            int frames = _measureAfterCanvasRefresh ? Mathf.Max(0, _measureDelayFrames - 1) : Mathf.Max(1, _measureDelayFrames);
            _pendingMeasure[key] = Time.frameCount + frames;
        }

        /// <inheritdoc/>
        public void MarkItemDirty(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= _measuredItems.Length) return;
            foreach (var key in _active.Keys)
            {
                if (key.Index != visibleIndex) continue;
                _measuredItems[visibleIndex] = false;
                EnqueueMeasure(key);
            }
        }

        private IEnumerator MeasureRoutine()
        {
            while (isActiveAndEnabled)
            {
                if (_measureAfterCanvasRefresh)
                    yield return _endOfFrame;
                else
                    yield return null;

                ProcessPendingMeasure();
            }
        }

        private void ProcessPendingMeasure()
        {
            if (!IsInitialized || _updatingView || _dirtyLayout || _layout == null || _pendingMeasure.Count == 0) return;
            _updatingView = true;
            try
            {
                MeasureVisible(prepareLayout: false);
            }
            finally
            {
                _updatingView = false;
            }
            if (!_dirtyLayout && !_pendingRelayout) PublishScroll(false);
        }

        private bool MeasureVisible(bool prepareLayout)
        {
            _measureBuffer.Clear();
            int frame = Time.frameCount;
            foreach (var kv in _pendingMeasure)
                if (kv.Value <= frame) _measureBuffer.Add(kv.Key);
            if (_measureBuffer.Count == 0) return false;

            bool anyResized = false;
            bool builtInLayout = _layout is IBufferedVirtualLayout;
            var anchor = _visibleBuffer.Count > 0 ? _visibleBuffer[0] : null;
            float anchorOffset = anchor != null ? _layout.GetScrollOffsetForIndex(anchor.Index) : 0f;
            _measuredThisPass.Clear();
            var batch = _layout as IBatchedVirtualLayout;
            batch?.BeginResizeBatch();
            try
            {
                for (int bufferIndex = 0; bufferIndex < _measureBuffer.Count && !_dirtyLayout; bufferIndex++)
                {
                    var key = _measureBuffer[bufferIndex];
                    if (!_pendingMeasure.Remove(key) || !_active.TryGetValue(key, out var active) || active.Cell == null
                        || HasMeasured(key.Index) || _measuredThisPass.Contains(key.Index)) continue;

                    if (prepareLayout)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(active.Transform);
                        if (_dirtyLayout) break;
                    }
                    Vector2 preferred = _callbacks.MeasureItem(active.Cell, key.Index);
                    if (_dirtyLayout || (builtInLayout && !SizePrefixCache.IsValidLength(Axis(preferred)))) continue;
                    if (builtInLayout && !_pendingMeasure.ContainsKey(key)) _measuredItems[key.Index] = true;
                    _measuredThisPass.Add(key.Index);
                    anyResized |= _layout.ResizeItem(key.Index, preferred);
                }
            }
            finally
            {
                if (batch != null) anyResized |= batch.EndResizeBatch();
            }

            if (!anyResized || _dirtyLayout) return false;
            AdjustAfterResize(anchor, anchorOffset);
            LayoutVisible();
            return true;
        }

        private void AdjustAfterResize(VirtualItemData anchor, float anchorOffset)
        {
            // 前缀尺寸或循环长度变化时，保持首个在屏条目的视口内位置。
            if (anchor != null && !_motion.IsAnimating)
            {
                float change = _layout.GetScrollOffsetForIndex(anchor.Index) - anchorOffset;
                float newLoopLength = GetLoopLength();
                if (_loopLength > 0f && newLoopLength > 0f)
                    change += (anchor.Cycle - _loopCycle) * (newLoopLength - _loopLength);
                _scrollOrigin += AlongAxis(change);
            }
            RetargetAnimation();
        }

        private bool HasMeasured(int index) => index >= 0 && index < _measuredItems.Length && _measuredItems[index];

        private void RetargetAnimation()
        {
            if (_motion.State != MotionState.Tween || _targetIndex < 0 || _targetIndex >= _layout.Count) return;

            float target = _targetAlignment.HasValue
                ? SnapOffset(_targetIndex, _targetAlignment.Value) : _layout.GetScrollOffsetForIndex(_targetIndex);
            float length = GetLoopLength();
            target += (_targetCycle - _loopCycle) * length;
            _motion.Looping = length > 0f;
            _motion.Bind(_scrollOrigin, _layout.ContentSize, _viewportSize, _layout.IsVertical);
            _motion.RetargetTween(target);
        }

        private void Release((long Cycle, int Index) key)
        {
            if (!_active.TryGetValue(key, out var active)) return;
            _active.Remove(key);
            _pendingMeasure.Remove(key);
            Recycle(active);
        }

        private void Recycle(ActiveCell active)
        {
            if (active.Instance == null) return;
            if (active.Cell != null)
            {
                try
                {
                    _callbacks.RecycleItem?.Invoke(active.Cell, active.Cell.VisibleIndex);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
                finally
                {
                    _pool.Release(active.Instance, active.TemplateId);
                    if (active.Cell != null) active.Cell.ClearIndex();
                }
                return;
            }
            active.Instance.SetActive(false);
            Destroy(active.Instance);
        }

        private float Axis(Vector2 value) => _layout.IsVertical ? value.y : value.x;

        private Vector2 AlongAxis(float value) => _layout.IsVertical ? new Vector2(0f, value) : new Vector2(value, 0f);

        private float GetLoopLength()
        {
            if (!_loopScrolling || _layout.Count == 0) return 0f;
            var padding = _layoutOptions.Padding;
            if (_layout.IsVertical ? padding.Top < 0 || padding.Bottom < 0 : padding.Left < 0 || padding.Right < 0)
                return 0f;

            float length = Axis(_layout.ContentSize) + SizePrefixCache.NonNegative(_layoutOptions.Spacing);
            return length >= 1f && !float.IsInfinity(length) ? length : 0f;
        }

        private void ConstrainScroll()
        {
            float offset = FiniteOrZero(Axis(_scrollOrigin));
            _loopLength = GetLoopLength();
            _motion.Looping = _loopLength > 0f;
            if (_motion.Looping)
            {
                if (offset < 0f || offset >= _loopLength)
                {
                    double turns = Math.Floor((double)offset / _loopLength);
                    float normalized = offset % _loopLength;
                    if (normalized < 0f) normalized += _loopLength;
                    if (normalized >= _loopLength)
                    {
                        normalized = 0f;
                        turns++;
                    }
                    if (turns <= long.MinValue || turns >= long.MaxValue)
                    {
                        RecycleAll();
                        _loopCycle = 0;
                        _motion.CancelMotion();
                    }
                    else _loopCycle += (long)turns;
                    _motion.ShiftOrigin(normalized - offset);
                    offset = normalized;
                }
                _scrollOrigin = AlongAxis(offset);
                return;
            }

            _loopCycle = 0;
            float over = _layout.Count > 0 && _scrollSettings.RubberbandEnabled && _motion.State != MotionState.Idle ? 80f : 0f;
            float end = Mathf.Max(0f, Axis(_layout.ContentSize) - Axis(_viewportSize));
            _scrollOrigin = AlongAxis(Mathf.Clamp(offset, -over, end + over));
        }

        private void RefreshScrollbars(Vector2 size)
        {
            UpdateScrollbar(_verticalScrollbar, true, size.y, _viewportSize.y, _scrollOrigin.y, _layout.IsVertical ? _loopLength : 0f);
            UpdateScrollbar(_horizontalScrollbar, false, size.x, _viewportSize.x, _scrollOrigin.x, _layout.IsVertical ? 0f : _loopLength);
        }

        private static float MapScrollbarValue(Scrollbar scrollbar, bool vertical, float value)
        {
            bool reverse = vertical ? scrollbar.direction == Scrollbar.Direction.BottomToTop
                : scrollbar.direction == Scrollbar.Direction.RightToLeft;
            return reverse ? 1f - value : value;
        }

        private static void UpdateScrollbar(Scrollbar scrollbar, bool vertical, float total, float viewport, float offset, float loopLength)
        {
            if (scrollbar == null) return;
            float length = loopLength > 0f ? loopLength : total;
            scrollbar.size = length <= viewport ? 1f : Mathf.Clamp01(viewport / length);
            float range = ScrollbarRange(loopLength, length - viewport);
            float normalized = range > 0f ? Mathf.Clamp01(offset / range) : 0f;
            scrollbar.SetValueWithoutNotify(MapScrollbarValue(scrollbar, vertical, normalized));
        }

        private void OnVerticalScrollbarChanged(float value) => ScrollFromScrollbar(_verticalScrollbar, true, value);

        private void OnHorizontalScrollbarChanged(float value) => ScrollFromScrollbar(_horizontalScrollbar, false, value);

        private void ScrollFromScrollbar(Scrollbar scrollbar, bool vertical, float value)
        {
            if (_callbacks.BindItem == null || _layout.IsVertical != vertical) return;
            float range = ScrollbarRange(_loopLength, Axis(_layout.ContentSize - _viewportSize));
            float offset = MapScrollbarValue(scrollbar, vertical, value) * range;
            SetScroll(AlongAxis(offset));
        }

        // 滑块末端要落在本轮内，正好一轮的偏移会回绕到 0。
        private static float ScrollbarRange(float loopLength, float finiteRange)
            => loopLength > 0f ? loopLength * 0.99999994f : Mathf.Max(0f, finiteRange);

        /// <inheritdoc/>
        public void SetScroll(Vector2 origin)
        {
            _motion?.CancelMotion();
            _scrollOrigin = new Vector2(FiniteOrZero(origin.x), FiniteOrZero(origin.y));
            Relayout(notifyScroll: true);
        }

        public void AddScroll(Vector2 delta) => SetScroll(_scrollOrigin + delta);

        private static float FiniteOrZero(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        public void ApplyScrollSettings(ScrollSettings settings)
        {
            _scrollSettings = settings;
            if (_motion != null) _motion.Settings = settings;
        }

        public ScrollSettings CurrentSettings => _scrollSettings;
    }
}
