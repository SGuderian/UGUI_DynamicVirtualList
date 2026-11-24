using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public sealed class DemoJumpTests : MonoBehaviour
    {
        [SerializeField] private VirtualListAdapterBase _view;
        [SerializeField] private DemoDataBootstrap _data;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private InputField _index;
        [SerializeField] private InputField _slot;
        [SerializeField] private InputField _offset;
        [SerializeField] private Toggle _precise;
        [SerializeField] private Toggle _smooth;
        [SerializeField] private Text _result;
        [SerializeField] private Text _stressButtonLabel;
        [SerializeField] private float _rowSpacing = 4f;

        private const int StressCases = 96;
        private const int StressSeed = 723;
        private const float Tolerance = 0.5f;

        private enum JumpKind { Top, Bottom, Slot, Offset }

        private readonly List<VirtualItemCell> _cells = new List<VirtualItemCell>();
        private readonly Vector3[] _corners = new Vector3[4];
        private Coroutine _stressRun;

        private float JumpDuration => _smooth.isOn ? Mathf.Max(0f, _view.CurrentSettings.SmoothDuration) : 0f;

        public void JumpToTop() => JumpToEdge(ScrollIndexAlignment.Top);
        public void JumpToBottom() => JumpToEdge(ScrollIndexAlignment.Bottom);

        private void JumpToEdge(ScrollIndexAlignment alignment)
        {
            StopStress();
            if (!TryReadIndex(out int index)) return;
            _result.text = $"Requested index {index} at {alignment}.";
            _view.ScrollToIndex(index, alignment, JumpDuration, _view.CurrentSettings.SmoothCurve,
                ShowJumpResult, precise: _precise.isOn);
        }

        public void JumpToSlot()
        {
            StopStress();
            if (!TryReadIndex(out int index)) return;
            if (!int.TryParse(_slot.text, out int slot) || slot < 0)
            {
                _result.text = "Enter a slot of 0 or higher.";
                return;
            }
            _result.text = $"Requested index {index} at slot {slot}.";
            _view.ScrollToIndex(index, JumpDuration, _view.CurrentSettings.SmoothCurve,
                ShowJumpResult, precise: _precise.isOn, viewportIndex: slot);
        }

        public void JumpToOffset()
        {
            StopStress();
            if (!_view.IsInitialized || _view.ItemCount == 0
                || !float.TryParse(_offset.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float offset)
                || float.IsNaN(offset) || float.IsInfinity(offset))
            {
                _result.text = "Enter a finite offset, for example 320.5.";
                return;
            }
            bool horizontal = _view.LayoutMode == VirtualLayoutMode.HorizontalList;
            _result.text = $"Requested {(horizontal ? "X" : "Y")} offset {offset:F1}.";
            _view.ScrollToPosition(horizontal ? new Vector2(offset, 0f) : new Vector2(0f, offset),
                JumpDuration, _view.CurrentSettings.SmoothCurve, ShowJumpResult, precise: _precise.isOn);
        }

        private void ShowJumpResult(Vector2 position)
        {
            if (_stressRun != null || !isActiveAndEnabled) return;
            bool horizontal = _view.LayoutMode == VirtualLayoutMode.HorizontalList;
            _result.text += $"\nReached {(horizontal ? "X" : "Y")} offset {(horizontal ? position.x : position.y):F1}.";
        }

        private bool TryReadIndex(out int index)
        {
            if (int.TryParse(_index.text, out index) && _view.IsInitialized && index >= 0 && index < _view.ItemCount)
                return true;
            _result.text = _view.ItemCount == 0 ? "The list is empty." : $"Enter an index from 0 to {_view.ItemCount - 1}.";
            return false;
        }

        public void ToggleStress()
        {
            if (_stressRun != null)
            {
                StopStress();
                _result.text += " (stopped)";
                return;
            }

            if (!_view.IsInitialized || _view.LoopScrolling)
            {
                _result.text = "Run the stress test with an initialized list and loop scrolling off.";
                return;
            }

            _stressButtonLabel.text = "Stop stress";
            _stressRun = StartCoroutine(RunStress());
        }

        private void OnDisable() => StopStress();

        private void StopStress()
        {
            if (_stressRun != null) StopCoroutine(_stressRun);
            FinishStress();
        }

        private void FinishStress()
        {
            _stressRun = null;
            if (_stressButtonLabel != null) _stressButtonLabel.text = "Run stress";
        }

        private IEnumerator RunStress()
        {
            _result.text = $"Stress 0/{StressCases}  |  seed {StressSeed}";
            yield return null;

            float height = _viewport.rect.height;
            int count = _view.ItemCount;
            int edgeMargin = Mathf.CeilToInt(height / DemoDataBootstrap.MinimumRowHeight) + 2;
            float maxOffset = _data.MinimumContentLength() - height - 1f;
            if (height <= 0f || edgeMargin * 2 >= count || maxOffset <= 0f)
            {
                _result.text = "Stress test needs a scrollable viewport and more rows.";
                FinishStress();
                yield break;
            }

            var random = new System.Random(StressSeed);
            int passed = 0;
            int failed = 0;
            string firstFailure = null;
            for (int i = 0; i < StressCases; i++)
            {
                if (!StressLayoutMatches(count, height))
                {
                    _result.text += " (layout changed)";
                    break;
                }

                int index = random.Next(edgeMargin, count - edgeMargin);
                bool smooth = (i / 4) % 2 == 0;
                var kind = (JumpKind)(i % 4);
                int slot = -1;
                float offset = 0f;
                string target;
                if (i % 8 == 0)
                    _view.ScrollToIndex(count - 1 - index, smooth: true, precise: true);

                switch (kind)
                {
                    case JumpKind.Top:
                        if (i == 0) index = 0;
                        _view.ScrollToIndex(index, ScrollIndexAlignment.Top, smooth, precise: true);
                        target = $"top index {index}";
                        break;
                    case JumpKind.Bottom:
                        if (i == 1) index = count - 1;
                        _view.ScrollToIndex(index, ScrollIndexAlignment.Bottom, smooth, precise: true);
                        target = $"bottom index {index}";
                        break;
                    case JumpKind.Slot:
                        slot = PickSlot(index, height, random);
                        _view.ScrollToIndex(index, smooth, precise: true, viewportIndex: slot);
                        target = $"slot {slot}, index {index}";
                        break;
                    default:
                        offset = i == 3 ? 0f : (float)random.NextDouble() * maxOffset;
                        _view.ScrollToPosition(new Vector2(0f, offset), smooth, precise: true);
                        target = $"offset {offset:F1}";
                        break;
                }

                float deadline = Time.realtimeSinceStartup + 5f;
                do
                {
                    yield return null;
                } while (_data.IsScrolling && StressLayoutMatches(count, height) && Time.realtimeSinceStartup < deadline);

                if (!StressLayoutMatches(count, height))
                {
                    _result.text += " (layout changed)";
                    break;
                }

                string error = _data.IsScrolling ? "timed out" : VerifyJump(kind, index, slot, offset);
                if (error == null)
                    passed++;
                else
                {
                    failed++;
                    if (firstFailure == null) firstFailure = $"Case {i + 1}, {target}: {error}";
                }
                _result.text = $"Stress {i + 1}/{StressCases}  |  {passed} passed, {failed} failed"
                    + (firstFailure == null ? "" : $"\n{firstFailure}");
            }
            FinishStress();
        }

        private bool StressLayoutMatches(int count, float height)
            => _view.IsInitialized && !_view.LoopScrolling && _view.ItemCount == count
                && Mathf.Abs(_viewport.rect.height - height) <= Tolerance;

        private int PickSlot(int index, float viewportHeight, System.Random random)
        {
            // 只要求视口能容纳的位次，给估算和实测高度都预留空间。
            float length = _data.MaximumRowHeight(index);
            int maxSlot = 0;
            while (maxSlot < 3 && index > maxSlot)
            {
                length += _rowSpacing + _data.MaximumRowHeight(index - maxSlot - 1);
                if (length + Tolerance > viewportHeight) break;
                maxSlot++;
            }
            return maxSlot > 0 ? random.Next(1, maxSlot + 1) : 0;
        }

        private string VerifyJump(JumpKind kind, int index, int slot, float offset)
        {
            if (kind == JumpKind.Offset)
            {
                if (!_view.TryGetViewportAnchor(out _, out _)) return "no visible anchor";
                return Mathf.Abs(_view.ScrollPosition.y - offset) <= Tolerance
                    ? null : $"actual offset {_view.ScrollPosition.y:F1}";
            }

            _viewport.GetComponentsInChildren(false, _cells);
            foreach (var cell in _cells)
            {
                if (cell.VisibleIndex != index) continue;

                ((RectTransform)cell.transform).GetWorldCorners(_corners);
                float top = _viewport.InverseTransformPoint(_corners[1]).y;
                float bottom = _viewport.InverseTransformPoint(_corners[0]).y;
                Rect viewportRect = _viewport.rect;
                float heightError = Mathf.Abs((top - bottom) - _data.RowHeight(index));
                if (heightError > Tolerance) return $"height error {heightError:F1}";

                if (kind == JumpKind.Slot)
                {
                    if (!_view.TryGetViewportAnchor(out int first, out _)) return "no visible anchor";
                    if (index - first != slot) return $"actual slot {index - first}";
                    if (slot > 0 && bottom < viewportRect.yMin - Tolerance) return "target clipped at bottom";
                    return null;
                }

                float edgeError = kind == JumpKind.Bottom ? bottom - viewportRect.yMin : top - viewportRect.yMax;
                return Mathf.Abs(edgeError) <= Tolerance ? null : $"edge error {edgeError:F1}";
            }
            return "target is outside the viewport";
        }
    }
}
