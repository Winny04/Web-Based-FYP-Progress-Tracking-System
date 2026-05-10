using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace FYPTracker.Components
{
    [ViewComponent(Name = "PageTitleViewComponent")] 
    public class PageTitleViewComponent : ViewComponent
    {
        #region Constructors

        public PageTitleViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/_PageTitle.cshtml");
        }
    }
}
