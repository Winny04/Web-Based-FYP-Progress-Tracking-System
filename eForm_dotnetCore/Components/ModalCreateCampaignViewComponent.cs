using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace eForm_dotnetCore.Components
{
    [ViewComponent(Name = "ModalCreateCampaignViewComponent")] 
    public class ModalCreateCampaignViewComponent : ViewComponent
    {
        #region Constructors

        public ModalCreateCampaignViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/Modals/Create-Campaign/_Main.cshtml");
        }
    }
}
