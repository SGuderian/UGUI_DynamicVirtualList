namespace VirtualList.Examples
{
    public sealed class DemoItem
    {
        public string Title { get; }
        public string Body { get; }
        public float EstimatedHeight { get; }
        public float LayoutHeight { get; }

        public DemoItem(string title, string body, float estimatedHeight, float layoutHeight = -1f)
        {
            Title = title;
            Body = body;
            EstimatedHeight = estimatedHeight;
            LayoutHeight = layoutHeight;
        }
    }
}
