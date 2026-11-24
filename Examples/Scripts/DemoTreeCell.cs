using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public class DemoTreeCell : AutoLayoutCell
    {
        [SerializeField] private Text _label;
        [SerializeField] private Text _expandArrow;

        public void Show(string label, bool hasChildren, bool expanded)
        {
            _label.text = label;
            _expandArrow.text = hasChildren ? (expanded ? "-" : "+") : "";
        }
    }
}
