using eForm_dotnetCore.Common;
using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.Others.Interfaces;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace eForm_dotnetCore.Services.Quartz
{
    public class DailyJobChecking : IJob
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IServer _server;

        public DailyJobChecking(EformSophicDemoContext context,
            IEmailService emailService,
            IServer server
            )
        {
            _context = context;
            _emailService = emailService;
            _server = server;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await SendETANotication();
        }

        public async Task SendETANotication()
        {
            var sc = new ServerContext(_server);
            var url = sc.GetServerAddress();
            var currentPr = 0;
            var tableContent = "";
            var todayDate = DateTime.Now.ToString("dd-MM-yyyy");
            var allContent = "";

            var getBoms = await _context.Boms
                .Where(x => x.EstDeliveryDate.HasValue && x.Status == "PO Approved" && x.PRId.HasValue)
                .Include(x => x.Project)
                .OrderBy(x => x.PRId)
                .ToListAsync();

            for (var index = 0; index <= getBoms.Count; index++)
            {
                var formatDate = getBoms[index].EstDeliveryDate.Value.AddDays(1).ToString("dd-MM-yyyy");

                if (index == 0) currentPr = getBoms[index].PRId.Value;

                if (currentPr != getBoms[index].PRId.Value)
                {
                    var prRequestor = _context.Prs.FirstOrDefault(x => x.Id == currentPr);

                    currentPr = getBoms[index].PRId.Value;
                    //send mail
                    if(prRequestor != null)
                    {
                        var checkEmails = _context.EmployeeInfos.Where(x => !string.IsNullOrEmpty(x.Email)
                        && x.Id == prRequestor.RequestorId && x.IsNotificationOn)
                            .Select(x => x.Email)
                            .FirstOrDefault();

                        if (!string.IsNullOrEmpty(checkEmails) && !string.IsNullOrEmpty(tableContent))
                        {

                            var generateMailTemp = TableBOMEmailTemp(tableContent, "BOM Item ETA Info");
                            _emailService.Send(checkEmails, "BOM Item ETA Info", generateMailTemp);
                        }

                    }

                    if (formatDate == todayDate)
                    {
                        tableContent = @$"<tr>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].Project.Name}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].BomNo}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].PartDesc}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].Status}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].BomNo}
                                             </td>
                                       </tr>
                                    ";
                    }
                        
                }
                else
                {
                    if(formatDate == todayDate)
                    {
                        tableContent += @$"<tr>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].Project.Name}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].BomNo}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].PartDesc}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].Status}
                                             </td>
                                            <td style='border: 1px solid black'>
                                                {getBoms[index].BomNo}
                                             </td>
                                       </tr>
                                    ";
                    }
                }

                allContent += tableContent;
            }

            //send to receiving
            var receivingUser = _context.EmployeeInfos.Where(x => x.UserName == "A1L002")
                .Select(x => x.Email).FirstOrDefault();

            if (!string.IsNullOrEmpty(receivingUser))
            {
                var generateMailTemp = TableBOMEmailTemp(allContent, "BOM Item ETA Info");
                _emailService.Send(receivingUser, "BOM Item ETA Info", generateMailTemp);
            }
        }

        private string TableBOMEmailTemp(string content,string title)
        {
            string message = $@"<p>Dear user,</p>
                                <p> Please find below BOM(s) item would be the last day for ETA.</p>";

            string tableContent = @$"<table class='table table-bordered' style='border: 1px solid black; text-align:center;font-size:14px;'>
                                    <thead>
                                    <tr>
                                        <th style='border: 1px solid black'>Project Name</th>
                                        <th style='border: 1px solid black'>BOM No</th>
                                        <th style='border: 1px solid black'>Description</th>
                                        <th style='border: 1px solid black;min-width: 150px;'>Status</th>
                                    </tr>
                                    </thead>
                                    <tbody>
                                        {content}
                                    </tbody>
                                </table>";

            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:900px"">
		                        <tbody>
			                        <tr>
				                        <td align=""center"" valign=""center"" style=""text-align:center;"">
						                        <img alt=""Logo"" src=""https://mymagic-central.s3.amazonaws.com/uploads/organization/thumbnail/21d15c5bc1086d561af1936c53deefcbfdf72fb2.adaptiveResize.320x320.png"" />
                                        </td>
			                        </tr>
			                        <tr>
				                        <td align=""left"" valign=""center"">
					                        <div style=""text-align:left; margin: 0 0px; padding: 40px; background-color:#ffffff; border-radius: 6px"">
						                        <!--begin:Email content-->
						                        <div style=""padding-bottom: 30px; font-size: 17px;"">
							                        <h3 style=""text-align:center;"">{title}</h3>
						                        </div>
                                                {tableContent}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return message;
        }
    }
}
