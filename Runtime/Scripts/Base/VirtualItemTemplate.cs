using System;
using UnityEngine;

namespace VirtualList
{
    [Serializable]
    public struct VirtualItemTemplate
    {
        public int TemplateId;
        public GameObject Prefab;

        public VirtualItemTemplate(int templateId, GameObject prefab)
        {
            TemplateId = templateId;
            Prefab = prefab;
        }
    }
}
