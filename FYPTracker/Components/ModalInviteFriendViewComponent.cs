using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace FYPTracker.Components
{
    [ViewComponent(Name = "ModalInviteFriendViewComponent")] 
    public class ModalInviteFriendViewComponent : ViewComponent
    {
        #region Constructors

        public ModalInviteFriendViewComponent()
        {
        }

        #endregion

        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View("/Views/Shared/Modals/_Invite-friends.cshtml");
        }
    }
}
