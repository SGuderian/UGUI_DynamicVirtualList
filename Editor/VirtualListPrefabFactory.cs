#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VirtualList.Examples;
using Object = UnityEngine.Object;

namespace VirtualList.EditorTools
{
    public enum FactoryMode
    {
        VerticalList,
        HorizontalList,
        Grid,
        Tree,
    }

    public static class VirtualListPrefabFactory
    {
        public const string DefaultPrefabFolder = "Assets/VirtualList/Generated";

        /// <summary>生成可直接运行的示例列表，并保存它使用的条目预制体。</summary>
        public static GameObject Generate(FactoryMode mode, Transform parent, int columnCount,
            int itemCount, bool withScrollbar, string prefabFolder, bool loopScrolling = false, bool orderVisibleNodes = true)
        {
            string folder = EnsurePrefabFolder(prefabFolder);
            parent = EnsureCanvasAndEventSystem(parent);
            GameObject itemPrefab = BuildItemPrefab(mode, folder, parent.gameObject.layer);
            if (itemPrefab == null) throw new InvalidOperationException("无法保存列表条目预制体。");
            var root = CreateHierarchy(mode, parent, columnCount, withScrollbar, GetAdapterType(mode), true,
                loopScrolling, orderVisibleNodes, out var controller);
            BindTemplates(controller, new[] { new VirtualItemTemplate(0, itemPrefab) });
            BindBootstrap(root.AddComponent<DemoDataBootstrap>(), controller, itemCount);
            RegisterRoot(root);
            AssetDatabase.SaveAssets();
            return root;
        }

        /// <summary>以业务提供的模板生成列表。回调注册和初始化由业务负责。</summary>
        public static GameObject GenerateProduction(FactoryMode mode, Transform parent, int columnCount, bool withScrollbar,
            Type controllerType, IReadOnlyList<VirtualItemTemplate> templates, bool loopScrolling = false, bool orderVisibleNodes = true)
        {
            Type adapterType = GetAdapterType(mode);
            controllerType = controllerType ?? adapterType;
            ValidateProduction(adapterType, controllerType, templates);
            parent = EnsureCanvasAndEventSystem(parent);
            var root = CreateHierarchy(mode, parent, columnCount, withScrollbar, controllerType, false,
                loopScrolling, orderVisibleNodes, out var controller);
            BindTemplates(controller, templates);
            RegisterRoot(root);
            return root;
        }

        private static Type GetAdapterType(FactoryMode mode)
        {
            switch (mode)
            {
                case FactoryMode.Grid: return typeof(GridScrollView);
                case FactoryMode.Tree: return typeof(TreeScrollView);
                default: return typeof(ListScrollView);
            }
        }

        private static void ValidateProduction(Type adapterType, Type controllerType, IReadOnlyList<VirtualItemTemplate> templates)
        {
            if (!adapterType.IsAssignableFrom(controllerType) || controllerType.IsAbstract || controllerType.ContainsGenericParameters)
                throw new ArgumentException($"Choose {adapterType.Name} or a concrete subclass for this layout.");
            if (templates == null || templates.Count == 0)
                throw new ArgumentException("Assign at least one item prefab.");

            var templateIds = new HashSet<int>();
            foreach (var template in templates)
            {
                if (!templateIds.Add(template.TemplateId))
                    throw new ArgumentException($"Template ID {template.TemplateId} is duplicated.");

                var prefab = template.Prefab;
                if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab) || prefab.transform.parent != null
                    || !(prefab.transform is RectTransform) || prefab.GetComponent<VirtualItemCell>() == null)
                    throw new ArgumentException($"Template {template.TemplateId} needs a prefab asset with a RectTransform and VirtualItemCell on its root.");
            }
        }

        private static void BindTemplates(Component controller, IReadOnlyList<VirtualItemTemplate> templates)
        {
            var serializedController = new SerializedObject(controller);
            var serializedTemplates = serializedController.FindProperty("_itemTemplates");
            serializedTemplates.arraySize = templates.Count;
            for (int index = 0; index < templates.Count; index++)
            {
                var entry = serializedTemplates.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative(nameof(VirtualItemTemplate.TemplateId)).intValue = templates[index].TemplateId;
                entry.FindPropertyRelative(nameof(VirtualItemTemplate.Prefab)).objectReferenceValue = templates[index].Prefab;
            }
            serializedController.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateHierarchy(FactoryMode mode, Transform parent, int columnCount,
            bool withScrollbar, Type controllerType, bool isDemo, bool loopScrolling, bool orderVisibleNodes, out Component controller)
        {
            var root = CreateRectChild(mode.ToString(), parent);
            Stretch(root);

            var viewport = CreateRectChild("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = isDemo ? new Color(0.94f, 0.94f, 0.94f) : Color.clear;

            var content = CreateRectChild("Content", viewport.transform);
            Stretch(content);

            Scrollbar scrollbar = null;
            if (withScrollbar)
            {
                scrollbar = BuildScrollbar(mode, root);
                if (mode == FactoryMode.HorizontalList) viewport.offsetMin = new Vector2(0f, 16f);
                else viewport.offsetMax = new Vector2(-16f, 0f);
            }

            controller = viewport.gameObject.AddComponent(controllerType);
            BindView(controller, viewport, content);
            BindControllerScrollbars(controller, mode, scrollbar);
            BindLayoutOptions(controller, mode, columnCount, loopScrolling, orderVisibleNodes);
            return root.gameObject;
        }

        private static void RegisterRoot(GameObject root)
        {
            Undo.RegisterCreatedObjectUndo(root, "Generate Virtual List");
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static GameObject BuildItemPrefab(FactoryMode mode, string folder, int layer)
        {
            bool isTree = mode == FactoryMode.Tree;

            var cell = new GameObject("Item", typeof(RectTransform)) { layer = layer };
            var rt = cell.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 60f);
            cell.AddComponent<Image>().color = new Color(1f, 1f, 1f, isTree ? 0.6f : 0.015f);

            var group = cell.AddComponent<VerticalLayoutGroup>();
            group.padding = new UnityEngine.RectOffset(8, 8, 6, 6);
            group.spacing = 4f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            if (isTree)
            {
                var arrow = CreateText("Arrow", cell.transform, "▸", TextAnchor.MiddleLeft, 20);
                var label = CreateText("Label", cell.transform, "节点", TextAnchor.MiddleLeft, 16);
                var treeCell = cell.AddComponent<DemoTreeCell>();
                BindObject(treeCell, "_label", label);
                BindObject(treeCell, "_expandArrow", arrow);
            }
            else
            {
                var title = CreateText("Title", cell.transform, "标题", TextAnchor.MiddleLeft, 16);
                var body = CreateText("Body", cell.transform, "描述文本", TextAnchor.UpperLeft, 13);
                var listCell = cell.AddComponent<DemoCell>();
                BindObject(listCell, "_title", title);
                BindObject(listCell, "_body", body);
                if (mode == FactoryMode.HorizontalList)
                {
                    var serializedCell = new SerializedObject(listCell);
                    serializedCell.FindProperty("_axis").enumValueIndex = (int)MeasureAxis.DynamicWidth;
                    serializedCell.ApplyModifiedProperties();
                }
            }

            string fileName = $"{mode}Item.prefab";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(cell, path);
            }
            finally
            {
                Object.DestroyImmediate(cell);
            }
        }

        private static Scrollbar BuildScrollbar(FactoryMode mode, Transform root)
        {
            var barRt = CreateRectChild("Scrollbar", root);
            var barGo = barRt.gameObject;

            bool vertical = mode != FactoryMode.HorizontalList;
            if (vertical)
            {
                barRt.anchorMin = new Vector2(1f, 0f);
                barRt.anchorMax = new Vector2(1f, 1f);
                barRt.pivot = new Vector2(1f, 0.5f);
                barRt.sizeDelta = new Vector2(12f, 0f);
                barRt.anchoredPosition = new Vector2(-4f, 0f);
            }
            else
            {
                barRt.anchorMin = new Vector2(0f, 0f);
                barRt.anchorMax = new Vector2(1f, 0f);
                barRt.pivot = new Vector2(0.5f, 0f);
                barRt.sizeDelta = new Vector2(0f, 12f);
                barRt.anchoredPosition = new Vector2(0f, 2f);
            }

            barGo.AddComponent<Image>().color = new Color(0.75f, 0.75f, 0.75f);
            var bar = barGo.AddComponent<Scrollbar>();
            bar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;

            var handle = CreateRectChild("Handle", barRt);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.35f, 0.35f, 0.35f);
            Stretch(handle);
            bar.targetGraphic = handleImage;
            bar.handleRect = handle.GetComponent<RectTransform>();
            return bar;
        }

        private static void BindView(Component controller, RectTransform viewport, RectTransform content)
        {
            BindObject(controller, "_viewport", viewport);
            BindObject(controller, "_content", content);
        }

        private static void BindControllerScrollbars(Component controller, FactoryMode mode, Scrollbar bar)
        {
            if (bar == null) return;
            if (mode == FactoryMode.HorizontalList)
                BindObject(controller, "_horizontalScrollbar", bar);
            else
                BindObject(controller, "_verticalScrollbar", bar);
        }

        private static void BindBootstrap(DemoDataBootstrap bootstrap, Component controller, int itemCount)
        {
            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("_itemCount").intValue = Mathf.Max(0, itemCount);
            serializedBootstrap.FindProperty("_view").objectReferenceValue = controller;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindLayoutOptions(Component controller, FactoryMode mode, int columnCount, bool loopScrolling, bool orderVisibleNodes)
        {
            var so = new SerializedObject(controller);
            if (controller is ListScrollView)
                so.FindProperty("_horizontal").boolValue = mode == FactoryMode.HorizontalList;
            if (controller is GridScrollView)
                so.FindProperty("_columnCount").intValue = Mathf.Max(1, columnCount);
            so.FindProperty("_loopScrolling").boolValue = loopScrolling;
            so.FindProperty("_orderVisibleNodes").boolValue = orderVisibleNodes;
            so.ApplyModifiedProperties();
        }

        private static void BindObject(Component comp, string field, Object value)
        {
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
        }

        private static RectTransform CreateRectChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Text CreateText(string name, Transform parent, string text, TextAnchor anchor, int fontSize)
        {
            var txt = CreateRectChild(name, parent).gameObject.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = fontSize;
            txt.alignment = anchor;
            txt.color = Color.black;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;
            return txt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static string EnsurePrefabFolder(string prefabFolder)
        {
            string folder = string.IsNullOrWhiteSpace(prefabFolder) ? DefaultPrefabFolder : prefabFolder.Trim();
            string[] parts = folder.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts[0] != "Assets") throw new ArgumentException("预制体保存目录必须位于 Assets 下。");
            foreach (string part in parts)
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("预制体保存目录包含无效的路径片段。");

            string current = "Assets";
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string created = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(current, parts[index]));
                    if (string.IsNullOrEmpty(created)) throw new InvalidOperationException($"无法创建目录：{next}");
                    next = created;
                }
                current = next;
            }
            return current;
        }

        private static Transform EnsureCanvasAndEventSystem(Transform parent)
        {
            var selectedCanvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            var canvas = selectedCanvas != null ? selectedCanvas : Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(canvasGo, "Create Virtual List Canvas");
            }

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create Virtual List Event System");
            }
            return selectedCanvas != null && parent is RectTransform ? parent : canvas.transform;
        }
    }
}
#endif
