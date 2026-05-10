using eForm_dotnetCore.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace eForm_dotnetCore.Components
{
    public class OverviewCardViewComponent:ViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync(string bg,string symbol,string title,int value)
        {
            var items = new CardModel(bg,symbol, title, value);

            return View(items);
        }
    }
}
