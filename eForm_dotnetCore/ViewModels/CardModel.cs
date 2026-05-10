namespace eForm_dotnetCore.ViewModels
{
    public class CardModel
    {
        public CardModel(string bgColor,string? symbol, string? title, int? value)
        {
            this.bgColor = bgColor;
            this.symbol = symbol;
            this.title = title;
            this.value = value;
        }
        public string? bgColor { get; set; }
        public string? symbol { get; set; }
        public string? title { get; set; }
        public int? value { get; set; }
    }
}
