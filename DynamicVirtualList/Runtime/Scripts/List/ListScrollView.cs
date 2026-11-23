using UnityEngine;

namespace VirtualList
{
    [AddComponentMenu("Virtual List/List Scroll View")]
    public class ListScrollView : VirtualListAdapterBase
    {
        [SerializeField] private bool _horizontal;

        public override VirtualLayoutMode LayoutMode
            => _horizontal ? VirtualLayoutMode.HorizontalList : VirtualLayoutMode.VerticalList;

        protected override IVirtualLayout CreateLayout() => new ListLayout(!_horizontal);
    }
}
