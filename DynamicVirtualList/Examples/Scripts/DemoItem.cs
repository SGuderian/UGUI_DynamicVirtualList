namespace VirtualList.Examples
{
    public sealed class DemoItem
    {
        public string Title { get; }
        public string Body { get; }
        public float EstimatedHeight { get; }

        public DemoItem(string title, string body, float estimatedHeight)
        {
            Title = title;
            Body = body;
            EstimatedHeight = estimatedHeight;
        }
    }
}
