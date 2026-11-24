using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public sealed class FittedDemoCell : DemoCell
    {
        [SerializeField] private RectTransform _fittedContent;

        // 普通模板由子节点的 Fitter 定高。详情模板由根节点的布局组把高度传给内容。
        public override Vector2 GetPreferredSize() => new Vector2(CellRect.rect.width, _fittedContent.rect.height);

        internal void SetTestHeight(float height)
        {
            var layout = _fittedContent.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
        }
    }
}
