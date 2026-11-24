using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualList
{
    public abstract partial class VirtualListAdapterBase
    {
        private RectTransform ValidateViewportAndContent()
        {
            if (!(transform is RectTransform root))
                throw ConfigurationError("The adapter GameObject needs a RectTransform.");

            var viewport = _viewport != null ? _viewport : root;
            if (viewport != root && !viewport.IsChildOf(root))
                throw ConfigurationError($"Viewport '{viewport.name}' must be on the adapter GameObject or one of its descendants.");

            if (_content != null)
            {
                if (_content == viewport || !_content.IsChildOf(viewport))
                    throw ConfigurationError($"Content '{_content.name}' must be a descendant of Viewport '{viewport.name}'.");

                foreach (var layout in _content.GetComponents<LayoutGroup>())
                {
                    if (layout.enabled)
                        throw ConfigurationError($"Content '{_content.name}' has an enabled {layout.GetType().Name}. The adapter positions its direct children.");
                }
            }

            if (_verticalScrollbar != null && _verticalScrollbar == _horizontalScrollbar)
                throw ConfigurationError($"Vertical Scrollbar and Horizontal Scrollbar both refer to '{_verticalScrollbar.name}'.");

            ValidateScrollbar(_verticalScrollbar, vertical: true);
            ValidateScrollbar(_horizontalScrollbar, vertical: false);
            return viewport;
        }

        private void ValidateScrollbar(Scrollbar scrollbar, bool vertical)
        {
            if (scrollbar == null) return;
            bool validDirection = vertical
                ? scrollbar.direction == Scrollbar.Direction.BottomToTop || scrollbar.direction == Scrollbar.Direction.TopToBottom
                : scrollbar.direction == Scrollbar.Direction.LeftToRight || scrollbar.direction == Scrollbar.Direction.RightToLeft;

            if (!validDirection)
            {
                string axis = vertical ? "Vertical" : "Horizontal";
                string expected = vertical ? "BottomToTop or TopToBottom" : "LeftToRight or RightToLeft";
                throw ConfigurationError($"{axis} Scrollbar '{scrollbar.name}' uses {scrollbar.direction}. Expected {expected}.");
            }
        }

        private Dictionary<int, GameObject> BuildValidatedPrefabMap(RectTransform viewport)
        {
            var custom = GetPrefabMap();
            var prefabs = custom == null ? new Dictionary<int, GameObject>() : new Dictionary<int, GameObject>(custom);
            if (_itemTemplates != null)
            {
                var ids = new HashSet<int>();
                for (int i = 0; i < _itemTemplates.Count; i++)
                {
                    var entry = _itemTemplates[i];
                    if (!ids.Add(entry.TemplateId))
                        throw ConfigurationError($"Item Templates contains duplicate ID {entry.TemplateId} at entry {i}.");
                    if (entry.Prefab == null)
                        throw ConfigurationError($"Item Templates entry {i} (ID {entry.TemplateId}) has no prefab assigned.");

                    prefabs[entry.TemplateId] = entry.Prefab;
                }
            }

            if (prefabs.Count == 0)
                throw ConfigurationError("No item templates are configured. Assign a prefab in Item Templates or provide one from GetPrefabMap().");

            foreach (var entry in prefabs)
            {
                var prefab = entry.Value;
                if (prefab == null)
                    throw ConfigurationError($"Item template ID {entry.Key} has no prefab assigned.");
                if (!(prefab.transform is RectTransform rect))
                    throw ConfigurationError($"Item template ID {entry.Key} ('{prefab.name}') needs a RectTransform on its root.");
                if (viewport.IsChildOf(rect) || (_content != null && _content.IsChildOf(rect)))
                    throw ConfigurationError($"Item template ID {entry.Key} ('{prefab.name}') contains the Viewport or Content and would clone the list hierarchy.");
                if (prefab.GetComponent<VirtualItemCell>() == null)
                    throw ConfigurationError($"Item template ID {entry.Key} ('{prefab.name}') needs a VirtualItemCell on its root.");
            }
            return prefabs;
        }

        private void ValidateLayoutConfiguration(LayoutOptions options)
        {
            if (options.Spacing < 0f || float.IsNaN(options.Spacing) || float.IsInfinity(options.Spacing))
                throw ConfigurationError($"Spacing must be a finite, non-negative number. Current value: {options.Spacing}.");
            if (_measureDelayFrames < 0)
                throw ConfigurationError($"Measure Delay Frames must be non-negative. Current value: {_measureDelayFrames}.");
            if (options.Mode == VirtualLayoutMode.Grid && options.ColumnCount < 1)
                throw ConfigurationError($"Grid Column Count must be at least 1. Current value: {options.ColumnCount}.");
            if (options.Mode == VirtualLayoutMode.Tree &&
                (options.IndentPerDepth < 0f || float.IsNaN(options.IndentPerDepth) || float.IsInfinity(options.IndentPerDepth)))
                throw ConfigurationError($"Tree Indent Per Depth must be a finite, non-negative number. Current value: {options.IndentPerDepth}.");
        }

        private int ResolveTemplateId(int index)
        {
            // 建表时顺便检查离屏项，业务的模板选择回调每项只读一次。
            int templateId = _callbacks.GetTemplateId(index);
            ValidateTemplateId(_configuredPrefabs, templateId, index);
            return templateId;
        }

        private void ValidateTemplateId(Dictionary<int, GameObject> prefabs, int templateId, int index)
        {
            if (!prefabs.TryGetValue(templateId, out var prefab) || prefab == null)
                throw ConfigurationError($"Item at display index {index} requires template ID {templateId}, which is missing from Item Templates and GetPrefabMap().");
        }

        private InvalidOperationException ConfigurationError(string reason)
            => new InvalidOperationException($"{GetType().Name} on '{name}' has invalid configuration. {reason}");
    }
}
