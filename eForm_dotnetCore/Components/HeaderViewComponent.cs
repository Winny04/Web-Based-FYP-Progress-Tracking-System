using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace eForm_dotnetCore.Components
{
    [ViewComponent(Name = "HeaderViewComponent")] 
    public class HeaderViewComponent : ViewComponent
    {
        #region Constructors

        public HeaderViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/_Menu.cshtml");
        }
    }
}
