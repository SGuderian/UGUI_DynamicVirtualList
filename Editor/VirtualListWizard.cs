#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VirtualList.EditorTools
{
    public class VirtualListWizard : EditorWindow
    {
        private static readonly string[] PurposeOptions = { "正式列表", "示例 Demo" };
        private FactoryMode _mode = FactoryMode.VerticalList;
        private bool _isDemo;
        private MonoScript _controllerScript;
        private readonly List<VirtualItemTemplate> _templates = new List<VirtualItemTemplate> { new VirtualItemTemplate(0, null) };
        private int _columnCount = 2;
        private int _itemCount = 30;
        private bool _withScrollbar = true;
        private bool _loopScrolling;
        private bool _orderVisibleNodes = true;
        private string _prefabFolder = VirtualListPrefabFactory.DefaultPrefabFolder;
        private bool _saveRootAsPrefab = true;
        private Vector2 _scroll;

        [MenuItem("Tools/VirtualList/生成列表向导")]
        public static void Open()
        {
            var window = GetWindow<VirtualListWizard>(true, "VirtualList 生成器");
            window.minSize = new Vector2(400f, 340f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Label("虚拟列表生成", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _isDemo = EditorGUILayout.Popup("生成用途", _isDemo ? 1 : 0, PurposeOptions) == 1;
            _mode = (FactoryMode)EditorGUILayout.EnumPopup("列表形式", _mode);

            using (new EditorGUI.DisabledScope(_mode != FactoryMode.Grid))
                _columnCount = EditorGUILayout.IntField("网格列数", _columnCount);

            if (_isDemo)
                _itemCount = EditorGUILayout.IntField("示例条目数量", _itemCount);
            else
                DrawProductionSettings();

            _withScrollbar = EditorGUILayout.Toggle("生成滚动条", _withScrollbar);
            _loopScrolling = EditorGUILayout.Toggle("循环滚动", _loopScrolling);
            _orderVisibleNodes = EditorGUILayout.Toggle("在屏节点按显示顺序排列", _orderVisibleNodes);
            _saveRootAsPrefab = EditorGUILayout.Toggle("同时把根节点存为预制体", _saveRootAsPrefab);
            if (_isDemo || _saveRootAsPrefab)
                _prefabFolder = EditorGUILayout.TextField("预制体保存目录", _prefabFolder);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("生成到场景", GUILayout.Height(32f)))
                Generate();
        }

        private void DrawProductionSettings()
        {
            EditorGUILayout.HelpBox("生成后，业务脚本直接调用 RegisterCallbacks 注册回调，再调用 Init 初始化适配器。", MessageType.Info);
            _controllerScript = (MonoScript)EditorGUILayout.ObjectField("控制器脚本（可选）", _controllerScript, typeof(MonoScript), false);
            GUILayout.Label("业务条目模板", EditorStyles.boldLabel);
            for (int index = 0; index < _templates.Count; index++)
            {
                var template = _templates[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("ID", GUILayout.Width(17f));
                    template.TemplateId = EditorGUILayout.IntField(template.TemplateId, GUILayout.Width(55f));
                    template.Prefab = (GameObject)EditorGUILayout.ObjectField(template.Prefab, typeof(GameObject), false);
                    if (GUILayout.Button("移除", GUILayout.Width(46f)))
                    {
                        _templates.RemoveAt(index);
                        break;
                    }
                }
                _templates[index] = template;
            }

            if (GUILayout.Button("添加条目模板"))
            {
                int nextId = 0;
                while (_templates.Exists(entry => entry.TemplateId == nextId)) nextId++;
                _templates.Add(new VirtualItemTemplate(nextId, null));
            }
        }

        private void Generate()
        {
            GameObject root;
            try
            {
                if (!_isDemo && _controllerScript != null && _controllerScript.GetClass() == null)
                    throw new System.ArgumentException("所选控制器脚本没有可用的组件类型。");
                string folder = _isDemo || _saveRootAsPrefab ? VirtualListPrefabFactory.EnsurePrefabFolder(_prefabFolder) : null;
                root = _isDemo
                    ? VirtualListPrefabFactory.Generate(_mode, Selection.activeTransform, _columnCount, _itemCount, _withScrollbar,
                        folder, _loopScrolling, _orderVisibleNodes)
                    : VirtualListPrefabFactory.GenerateProduction(_mode, Selection.activeTransform, _columnCount, _withScrollbar,
                        _controllerScript != null ? _controllerScript.GetClass() : null, _templates, _loopScrolling, _orderVisibleNodes);

                if (_saveRootAsPrefab)
                {
                    string name = _isDemo ? $"{_mode}Demo" : _mode.ToString();
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.prefab");
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new System.InvalidOperationException($"无法保存列表预制体 {path}。");
                }
            }
            catch (System.ArgumentException exception)
            {
                EditorUtility.DisplayDialog("无法生成列表", exception.Message, "确定");
                return;
            }
            catch (System.InvalidOperationException exception)
            {
                EditorUtility.DisplayDialog("无法生成列表", exception.Message, "确定");
                return;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log(_isDemo ? $"[VirtualList] 已生成 {_mode} 示例，条目数 {_itemCount}。"
                : $"[VirtualList] 已生成 {_mode} 正式列表。", root);
        }
    }
}
#endif
