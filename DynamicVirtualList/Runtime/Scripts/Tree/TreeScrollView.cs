using UnityEngine;

namespace VirtualList
{
    [AddComponentMenu("Virtual List/Tree Scroll View")]
    public class TreeScrollView : VirtualListAdapterBase
    {
        [SerializeField, Min(0f)] private float _indentPerDepth = 24f;

        public override VirtualLayoutMode LayoutMode => VirtualLayoutMode.Tree;

        protected override IVirtualLayout CreateLayout() => new TreeLayout();

        protected override void ConfigureLayout(ref LayoutOptions options) => options.IndentPerDepth = _indentPerDepth;
    }
}
