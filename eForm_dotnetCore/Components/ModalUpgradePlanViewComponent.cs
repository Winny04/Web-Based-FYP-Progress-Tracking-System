using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace eForm_dotnetCore.Components
{
    [ViewComponent(Name = "ModalUpgradePlanViewComponent")] 
    public class ModalUpgradePlanViewComponent : ViewComponent
    {
        #region Constructors

        public ModalUpgradePlanViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/Modals/_Upgrade-plan.cshtml");
        }
    }
}
