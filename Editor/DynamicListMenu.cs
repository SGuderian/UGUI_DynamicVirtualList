using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VirtualList.EditorTools
{
    internal static class DynamicListMenu
    {
        private const string MenuPath = "GameObject/DynamicList/";
        private const string TemplatePath = "Packages/com.dynamivituallist.runtime/Runtime/Prefabs/";

        [MenuItem(MenuPath + "Vertical List", false, 10)]
        private static void CreateVerticalList(MenuCommand command) => Create(command, "VerticalList");

        [MenuItem(MenuPath + "Horizontal List", false, 11)]
        private static void CreateHorizontalList(MenuCommand command) => Create(command, "HorizontalList");

        [MenuItem(MenuPath + "Grid", false, 12)]
        private static void CreateGrid(MenuCommand command) => Create(command, "Grid");

        [MenuItem(MenuPath + "Tree", false, 13)]
        private static void CreateTree(MenuCommand command) => Create(command, "Tree");

        private static void Create(MenuCommand command, string templateName)
        {
            string path = TemplatePath + templateName + ".prefab";
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (template == null)
            {
                Debug.LogError($"DynamicList template is missing: {path}");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            string undoName = $"Create {templateName}";
            Undo.SetCurrentGroupName(undoName);

            Transform parent = GetParent(command.context as GameObject, undoName);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(root);
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.layer == parent.gameObject.layer) continue;
                child.gameObject.layer = parent.gameObject.layer;
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }

            Undo.RegisterCreatedObjectUndo(root, undoName);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = root;
        }

        private static Transform GetParent(GameObject context, string undoName)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                return context != null ? context.transform : prefabStage.prefabContentsRoot.transform;

            var stage = StageUtility.GetCurrentStageHandle();
            var canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            Transform parent = canvas != null ? context.transform : null;
            if (canvas == null) canvas = stage.FindComponentOfType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
                {
                    layer = LayerMask.NameToLayer("UI")
                };
                StageUtility.PlaceGameObjectInCurrentStage(canvasObject);
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(canvasObject, undoName);
            }

            if (stage.FindComponentOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                StageUtility.PlaceGameObjectInCurrentStage(eventSystem);
                Undo.RegisterCreatedObjectUndo(eventSystem, undoName);
            }
            return parent != null ? parent : canvas.transform;
        }
    }
}
