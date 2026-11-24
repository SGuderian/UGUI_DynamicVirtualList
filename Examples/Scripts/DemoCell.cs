using UnityEngine;
using UnityEngine.UI;

namespace VirtualList.Examples
{
    public class DemoCell : AutoLayoutCell
    {
        [SerializeField] private Text _title;
        [SerializeField] private Text _body;

        public void Show(string title, string body)
        {
            _title.text = title;
            _body.text = body;
        }
    }
}
