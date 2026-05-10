using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace FYPTracker.Components
{
    [ViewComponent(Name = "FooterViewComponent")] 
    public class FooterViewComponent : ViewComponent
    {
        #region Constructors

        public FooterViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/_Footer.cshtml");
        }
    }
}
