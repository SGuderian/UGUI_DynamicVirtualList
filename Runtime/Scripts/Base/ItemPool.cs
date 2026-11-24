using System.Collections.Generic;
using UnityEngine;

namespace VirtualList
{
    /// <summary>按模板 ID 复用实例。回池时停用实例，保留其容器父节点。</summary>
    public sealed class ItemPool
    {
        private readonly Dictionary<int, Stack<GameObject>> _buckets = new Dictionary<int, Stack<GameObject>>();
        private readonly Transform _container;
        private readonly Dictionary<int, GameObject> _prefabs;

        public ItemPool(Transform container, Dictionary<int, GameObject> prefabs)
        {
            _container = container;
            _prefabs = prefabs;
        }

        internal bool MatchesConfiguration(Transform container, Dictionary<int, GameObject> prefabs)
        {
            if (_container != container) return false;
            if (ReferenceEquals(_prefabs, prefabs)) return true;
            if (_prefabs.Count != prefabs.Count) return false;
            foreach (var entry in prefabs)
                if (!_prefabs.TryGetValue(entry.Key, out var prefab) || prefab != entry.Value) return false;
            return true;
        }

        public GameObject Acquire(int templateId)
        {
            if (_buckets.TryGetValue(templateId, out var stack))
            {
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go == null) continue;
                    go.SetActive(true);
                    return go;
                }
            }

            if (!_prefabs.TryGetValue(templateId, out var prefab) || prefab == null)
                return null;

            var instance = Object.Instantiate(prefab, _container, false);
            instance.SetActive(true);
            return instance;
        }

        public void Release(GameObject instance, int templateId)
        {
            if (instance == null) return;
            instance.SetActive(false);
            if (!_buckets.TryGetValue(templateId, out var stack))
            {
                stack = new Stack<GameObject>();
                _buckets[templateId] = stack;
            }
            stack.Push(instance);
        }

        public void Clear()
        {
            foreach (var stack in _buckets.Values)
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go != null) Object.Destroy(go);
                }
            _buckets.Clear();
        }
    }
}
