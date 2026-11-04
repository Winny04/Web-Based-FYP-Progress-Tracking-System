namespace FYPTracker.Common
{
    public class SelectItem
    {
        public SelectItem(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; set; }

        public string Text { get; set; }
    }

    public static class BrandDisabledWords
    {
        public static IEnumerable<string> DisableWords => new List<string> { "na", "n/a", "-" };
    }
}
