using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public class DemoCell : AutoLayoutCell
    {
        [SerializeField] private Text _title;
        [SerializeField] private Text _body;
        [SerializeField] private LayoutElement _rowLayout;

        public void Show(string title, string body, float height = -1f)
        {
            _title.text = title;
            _body.text = body;
            if (_rowLayout != null)
            {
                _rowLayout.minHeight = height;
                _rowLayout.preferredHeight = height;
            }
        }
    }
}
