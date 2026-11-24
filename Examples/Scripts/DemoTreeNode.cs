using System.Collections.Generic;

namespace VirtualList.Examples
{
    public sealed class DemoTreeNode
    {
        public string Label { get; }
        public List<DemoTreeNode> Children { get; } = new List<DemoTreeNode>();
        public bool Expanded { get; set; }
        public int Depth { get; }

        public DemoTreeNode(string label, int depth, bool expanded)
        {
            Label = label;
            Depth = depth;
            Expanded = expanded;
        }
    }
}
