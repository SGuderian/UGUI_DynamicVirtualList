using UnityEngine;
using UnityEngine.EventSystems;

namespace VirtualList
{
    /// <summary>可复用单元格。条目内容由适配器注册的绑定回调填充。</summary>
    [RequireComponent(typeof(RectTransform))]
    public class VirtualItemCell : MonoBehaviour, IPointerClickHandler
    {
        private VirtualListAdapterBase _owner;
        /// <summary>当前显示索引，未绑定时为 -1。</summary>
        public int VisibleIndex { get; private set; } = -1;
        public int TemplateId { get; private set; }

        internal void Assign(VirtualListAdapterBase owner, int index, int templateId)
        {
            _owner = owner;
            VisibleIndex = index;
            TemplateId = templateId;
        }

        internal void ClearIndex()
        {
            _owner = null;
            VisibleIndex = -1;
        }

        /// <summary>将点击交给所属适配器。子类重写时可调用基类以保留点击回调。</summary>
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                _owner?.HandleItemClick(this);
        }

        /// <summary>供业务测量回调读取首选尺寸。默认的零尺寸不会覆盖布局缓存。</summary>
        public virtual Vector2 GetPreferredSize() => Vector2.zero;
    }
}
