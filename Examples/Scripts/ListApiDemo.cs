using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public sealed class ListApiDemo : MonoBehaviour
    {
        [SerializeField] private VirtualListAdapterBase _view;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private InputField _index;
        [SerializeField] private InputField _slot;
        [SerializeField] private InputField _offset;
        [SerializeField] private InputField _duration;
        [SerializeField] private InputField _defaultHeight;
        [SerializeField] private Toggle _precise;
        [SerializeField] private Toggle _loop;
        [SerializeField] private Toggle _inertia;
        [SerializeField] private Text _curveLabel;
        [SerializeField] private Text _viewportStatus;
        [SerializeField] private Text _eventLog;

        private const int InitialCount = 120;
        private const int LogCapacity = 7;

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<int> _refreshed = new List<int>();
        private readonly List<DemoCell> _visibleCells = new List<DemoCell>();
        private readonly Queue<string> _log = new Queue<string>();
        private IVirtualListAdapter _adapter;
        private System.Random _random;
        private EaseCurve _curve = EaseCurve.EaseOutCubic;
        private Vector2 _initialSize;
        private ScrollSettings _initialSettings;
        private int _bound;
        private int _measured;
        private int _recycled;
        private bool _showMovement;
        private bool _statusDirty;

        private sealed class Row
        {
            public int TemplateId;
            public int Revision;
            public float Height;
        }

        private void Awake()
        {
            _adapter = _view;
            _initialSize = _adapter.DefaultItemSize;
            _initialSettings = _adapter.CurrentSettings;
        }

        private void OnEnable() => Initialize();

        private void OnDisable() => _adapter.ClearCallbacks();

        public void ResetDemo()
        {
            _adapter.ClearCallbacks();
            _adapter.SetLoopScrolling(false);
            _adapter.DefaultItemSize = _initialSize;
            _adapter.ApplyScrollSettings(_initialSettings);
            _index.text = "70";
            _slot.text = "1";
            _offset.text = "620.5";
            _duration.text = "0.45";
            _precise.SetIsOnWithoutNotify(true);
            _curve = EaseCurve.EaseOutCubic;
            Initialize();
        }

        private void Initialize()
        {
            _random = new System.Random(428);
            _rows.Clear();
            _refreshed.Clear();
            _log.Clear();
            _bound = _measured = _recycled = 0;
            _showMovement = false;
            _loop.SetIsOnWithoutNotify(_adapter.LoopScrolling);
            _inertia.SetIsOnWithoutNotify(_adapter.CurrentSettings.InertiaEnabled);
            _defaultHeight.text = _adapter.DefaultItemSize.y.ToString(CultureInfo.InvariantCulture);
            _curveLabel.text = _curve.ToString();
            AppendRows(InitialCount);

            _adapter.RegisterCallbacks(BindItem, measureItem: MeasureItem, recycleItem: OnRecycleItem);
            _adapter.RegisterItemTemplateCallback(index => _rows[index].TemplateId);
            _adapter.RegisterItemClickCallback(OnItemClicked);
            _adapter.RegisterRefreshRequestedCallback(OnRefreshRequested);
            _adapter.RegisterItemRefreshedCallback(OnItemRefreshed);
            _adapter.RegisterListRefreshedCallback(OnListRefreshed);
            _adapter.RegisterScrollStartedCallback(OnScrollStarted);
            _adapter.RegisterScrollChangedCallback(OnScrollChanged);
            _adapter.RegisterScrollEndedCallback(OnScrollEnded);
            _adapter.Init(_rows.Count);
            Log($"Init({_rows.Count})");
        }

        private void AppendRows(int count)
        {
            for (int i = 0; i < count; i++)
            {
                bool detail = _rows.Count % 3 == 1;
                _rows.Add(new Row
                {
                    TemplateId = detail ? 1 : 0,
                    Height = detail ? _random.Next(110, 211) : _random.Next(48, 73)
                });
            }
        }

        private void BindItem(VirtualItemCell cell, int index)
        {
            _bound++;
            ShowRow((DemoCell)cell, index);
            _statusDirty = true;
        }

        private void ShowRow(DemoCell cell, int index)
        {
            var row = _rows[index];
            string template = row.TemplateId == 0 ? "Standard" : "Detail";
            cell.Show($"#{index}   {template}", $"Revision {row.Revision}   |   Height {row.Height:F0}", row.Height);
        }

        private Vector2 MeasureItem(VirtualItemCell cell, int index)
        {
            _measured++;
            _statusDirty = true;
            return cell.GetPreferredSize();
        }

        private void OnRecycleItem(VirtualItemCell cell, int index)
        {
            _recycled++;
            _statusDirty = true;
        }

        private void OnItemClicked(VirtualItemCell cell, int index)
        {
            _index.text = index.ToString();
            Log($"Item clicked: #{index}, template {cell.TemplateId}");
        }

        public void UseFirstVisible()
        {
            if (!_adapter.TryGetViewportAnchor(out int index, out float offset)) return;
            _index.text = index.ToString();
            Log($"Viewport anchor: #{index}, offset {offset:F1}");
        }

        public void JumpToTop() => JumpToEdge(ScrollIndexAlignment.Top);
        public void JumpToBottom() => JumpToEdge(ScrollIndexAlignment.Bottom);

        private void JumpToEdge(ScrollIndexAlignment edge)
        {
            if (!TryReadIndex(out int index) || !TryReadDuration(out float duration)) return;
            Log($"ScrollToIndex(#{index}, {edge})");
            _adapter.ScrollToIndex(index, edge, duration, _curve, OnJumpCompleted, precise: _precise.isOn);
        }

        public void JumpToSlot()
        {
            if (!TryReadIndex(out int index) || !TryReadDuration(out float duration)) return;
            if (!int.TryParse(_slot.text, out int slot) || slot < 0)
            {
                Log("Enter a slot of 0 or higher.");
                return;
            }

            Log($"ScrollToIndex(#{index}, slot {slot})");
            _adapter.ScrollToIndex(index, duration, _curve, OnJumpCompleted, precise: _precise.isOn, viewportIndex: slot);
        }

        public void JumpToOffset()
        {
            if (!TryReadOffset(out float offset) || !TryReadDuration(out float duration)) return;
            Log($"ScrollToPosition(Y {offset:F1})");
            _adapter.ScrollToPosition(new Vector2(0f, offset), duration, _curve, OnJumpCompleted, precise: _precise.isOn);
        }

        public void SetOffset()
        {
            if (!TryReadOffset(out float offset)) return;
            Log($"SetScroll(Y {offset:F1})");
            _adapter.SetScroll(new Vector2(0f, offset));
        }

        public void AddOffset()
        {
            if (!TryReadOffset(out float offset)) return;
            Log($"AddScroll(Y {offset:F1})");
            _adapter.AddScroll(new Vector2(0f, offset));
        }

        public void CycleCurve()
        {
            _curve = _curve == EaseCurve.Spring ? EaseCurve.Linear : (EaseCurve)((int)_curve + 1);
            _curveLabel.text = _curve.ToString();
        }

        private void OnJumpCompleted(Vector2 position) => Log($"Jump complete: Y {position.y:F1}");

        public void RefreshIndex()
        {
            if (!TryReadIndex(out int index)) return;
            _rows[index].Revision++;
            Log($"RefreshItem(#{index})");
            _adapter.RefreshItem(index);
        }

        public void RefreshKeepingViewport() => RefreshViewport(keepViewport: true);
        public void RefreshAtTarget() => RefreshViewport(keepViewport: false);

        private void RefreshViewport(bool keepViewport)
        {
            if (!TryReadIndex(out int index)) return;
            _rows[index].Revision++;
            Log($"RefreshItem(#{index}, keepViewport: {keepViewport.ToString().ToLowerInvariant()})");
            _adapter.RefreshItem(index, keepViewport);
        }

        public void ChangeHeight()
        {
            if (!TryReadIndex(out int index)) return;
            _viewport.GetComponentsInChildren(false, _visibleCells);
            bool found = false;
            foreach (var cell in _visibleCells)
            {
                if (cell.VisibleIndex != index) continue;
                if (!found)
                {
                    var row = _rows[index];
                    row.Height = row.Height < 160f ? row.Height + 48f : 56f;
                    row.Revision++;
                    found = true;
                }
                ShowRow(cell, index);
            }
            _visibleCells.Clear();
            if (!found)
            {
                Log("Choose a visible row to change its height.");
                return;
            }

            Log($"MarkItemDirty(#{index}), new height {_rows[index].Height:F0}");
            _adapter.MarkItemDirty(index);
        }

        public void RefreshList()
        {
            Log("RefreshItems()");
            _adapter.RefreshItems();
        }

        public void RequestMoreRows() => _adapter.RequestRefresh();

        private void OnRefreshRequested()
        {
            Log("RequestRefresh: business appended 6 rows.");
            AppendRows(6);
            _adapter.RefreshItems(_rows.Count);
        }

        public void ChangeTemplate()
        {
            if (!TryReadIndex(out int index)) return;
            _adapter.TryGetViewportAnchor(out int anchor, out float offset);
            // 重建先使用默认高度。若原偏移会跳过锚点，就从该条目的前沿开始。
            if (offset >= _adapter.DefaultItemSize.y) offset = 0f;
            var row = _rows[index];
            row.TemplateId = 1 - row.TemplateId;
            row.Revision++;
            Log($"#{index} uses template {row.TemplateId}. Rebuilding with anchor #{anchor}.");
            _adapter.RefreshItems(_rows.Count, anchor, offset);
        }

        public void ApplyDefaultHeight()
        {
            if (!TryReadFloat(_defaultHeight, out float height) || height <= 0f)
            {
                Log("Enter a default height greater than 0.");
                return;
            }

            _adapter.DefaultItemSize = new Vector2(_adapter.DefaultItemSize.x, height);
            Log($"DefaultItemSize.y = {height:F1}");
            _adapter.RefreshItems();
        }

        public void SetLoop(bool enabled)
        {
            _adapter.SetLoopScrolling(enabled);
            Log(enabled ? "Loop scrolling enabled." : "Loop scrolling disabled.");
        }

        public void SetInertia(bool enabled)
        {
            var settings = _adapter.CurrentSettings;
            settings.InertiaEnabled = enabled;
            _adapter.ApplyScrollSettings(settings);
            Log(enabled ? "Inertia enabled." : "Inertia disabled.");
        }

        private void OnItemRefreshed(int index) => _refreshed.Add(index);

        private void OnListRefreshed(int count) => Log($"List refreshed: {count} items");

        private void OnScrollStarted(Vector2 position)
        {
            _showMovement = true;
            Log($"Scroll started: Y {position.y:F1}");
        }

        private void OnScrollChanged(Vector2 position)
        {
            _statusDirty = true;
            if (!_showMovement) return;
            _showMovement = false;
            Log($"Scroll changed: Y {position.y:F1}");
        }

        private void OnScrollEnded(Vector2 position) => Log($"Scroll ended: Y {position.y:F1}");

        private void Log(string message)
        {
            if (_log.Count == LogCapacity) _log.Dequeue();
            _log.Enqueue(message);
            _eventLog.text = string.Join("\n", _log);
            _statusDirty = true;
        }

        private void LateUpdate()
        {
            if (_refreshed.Count > 0)
            {
                Log($"Item refreshed ({_refreshed.Count}): {string.Join(", ", _refreshed)}");
                _refreshed.Clear();
            }
            if (!_statusDirty) return;
            _statusDirty = false;
            _adapter.TryGetViewportAnchor(out int anchor, out _);
            _viewportStatus.text = $"Y {_adapter.ScrollPosition.y:F1}   |   First #{anchor}   |   {_adapter.ItemCount} items\n"
                + $"Binds {_bound}   ·   Measures {_measured}   ·   Recycles {_recycled}";
        }

        private bool TryReadIndex(out int index)
        {
            if (int.TryParse(_index.text, out index) && index >= 0 && index < _rows.Count) return true;
            Log($"Enter an index from 0 to {_rows.Count - 1}.");
            return false;
        }

        private bool TryReadOffset(out float offset)
        {
            if (TryReadFloat(_offset, out offset)) return true;
            Log("Enter a finite Y offset, for example 620.5.");
            return false;
        }

        private bool TryReadDuration(out float duration)
        {
            if (TryReadFloat(_duration, out duration) && duration >= 0f) return true;
            Log("Enter a duration of 0 or more seconds.");
            return false;
        }

        private static bool TryReadFloat(InputField field, out float value)
            => float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
