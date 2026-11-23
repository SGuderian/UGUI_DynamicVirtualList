using UnityEngine;

namespace VirtualList
{
    [AddComponentMenu("Virtual List/Grid Scroll View")]
    public class GridScrollView : VirtualListAdapterBase
    {
        [SerializeField, Min(1)] private int _columnCount = 2;

        public override VirtualLayoutMode LayoutMode => VirtualLayoutMode.Grid;

        protected override IVirtualLayout CreateLayout() => new GridLayout();

        protected override void ConfigureLayout(ref LayoutOptions options) => options.ColumnCount = _columnCount;
    }
}
