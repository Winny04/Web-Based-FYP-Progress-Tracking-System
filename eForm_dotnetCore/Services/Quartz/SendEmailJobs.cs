using eForm_dotnetCore.Models;
using eForm_dotnetCore.Services.Others.Interfaces;
using Quartz;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Web;
using eForm_dotnetCore.Common;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace eForm_dotnetCore.Services.Quartz
{
    public class SendEmailJobs : IJob
    {
        private readonly EformSophicDemoContext _context;
        private readonly IEmailService _emailService;
        private readonly IServer _server;

        public SendEmailJobs(EformSophicDemoContext context,
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
            //await PendingRFQReviewNotification();
            await PRApprovedNotification();
            //await PRPendingApprovalNotification();
            await BOMPendingCompletedNotification();
            //await TrySend();
        }

        //public async Task TrySend()
        //{
        //    var emails = _context.EmployeeInfos.Where(x => (x.Department == "SI" && x.UserType == "HOD") || x.UserType == "CEO" && x.Email != null).Select(x => x.Email).Distinct().ToList();

        //    await _emailService.Send(emails, "Test From EBOM", "Please dont reply this email!");
        //}

        public async Task PendingRFQReviewNotification()
        {
            try
            {
                var getBoms = _context.Boms.Where(x => x.Status == "RFQ Sent").ToList();
                var bomList = new List<Bom>();
                foreach (var bom in getBoms)
                {
                    DateTime bomLastUpdateDate = (DateTime)bom.LastUpdatedDate;
                    DateTime today = DateTime.Now.Date;
                    var totalDays = (today - bomLastUpdateDate.Date).TotalDays;
                    if (totalDays >= 2)
                    {
                        //Comment this part if wanna test..//
                        var checkLastEmailSent = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                        if (checkLastEmailSent != null)
                        {
                            DateTime bomLastSendDate = (DateTime)checkLastEmailSent.LastEmailSentDate;
                            var totalSentDays = (today - bomLastSendDate.Date).TotalDays;
                            if (totalSentDays >= 2)
                            {
                                bomList.Add(bom);
                            }
                        }
                        else
                        {
                            bomList.Add(bom);
                        }
                    }
                }

                if (bomList.Any())
                {
                    await SendPendingRFQEmail(bomList.OrderBy(x => x.ProjectId).ToList());
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }


        public async Task PRPendingApprovalNotification()
        {
            try
            {
                var getBoms = _context.Boms.Where(x => x.Status == "PR Sent").ToList();
                var filteredBoms = new List<Bom>();

                var prApproval = _context.PRApprover.ToList();

                var prApprovalHOD = prApproval.Where(x => x.Type == "HOD" && x.Approved == false).Select(x => x.PRId).ToList();
                var prApprovalGFC = prApproval.Where(x => x.Type == "GFC" && x.Approved == false).Select(x => x.PRId).ToList();
                var prApprovalCEO = prApproval.Where(x => x.Type == "CEO" && x.Approved == false).Select(x => x.PRId).ToList();
                var HODApproveBOMs = getBoms.Where(x => prApprovalHOD.Contains((int)x.PRId)).ToList();
                var GFCApproveBOMs = getBoms.Where(x => prApprovalGFC.Contains((int)x.PRId)).ToList();
                var CEOApproveBOMs = getBoms.Where(x => prApprovalCEO.Contains((int)x.PRId)).ToList();

                var targetUserType = new List<string>()
                {
                    "HOD","GFC","CEO"
                };

                var allDepartment = new List<string>()
                {
                    "BD","FE","FAS","OE","R&D","SI"
                };


                string sendToUserType = "";
                foreach (var usertype in targetUserType)
                {
                    if (usertype == "HOD")
                    {
                        filteredBoms = HODApproveBOMs;
                        sendToUserType = "HOD";
                    }
                    else if (usertype == "GFC")
                    {
                        filteredBoms = GFCApproveBOMs;
                        sendToUserType = "GFC";
                    }
                    else if (usertype == "CEO")
                    {
                        filteredBoms = CEOApproveBOMs;
                        sendToUserType = "CEO";
                    }



                    if (filteredBoms.Any())
                    {
                        var bomList = new List<Bom>();
                        var bomListBD = new List<Bom>();
                        var bomListFE = new List<Bom>();
                        var bomListFAS = new List<Bom>();
                        var bomListOE = new List<Bom>();
                        var bomListRnD = new List<Bom>();
                        var bomListSI = new List<Bom>();
                        foreach (var bom in filteredBoms)
                        {
                            DateTime bomLastUpdateDate = (DateTime)bom.LastUpdatedDate;
                            DateTime today = DateTime.Now.Date;
                            var totalhours = (today - bomLastUpdateDate.Date).TotalHours;
                            //var totalDays = (today - bomLastUpdateDate.Date).TotalHours;
                            if (totalhours >= 12)
                            {
                                //Comment this part if wanna test..//
                                var checkLastEmailSent = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                                if (checkLastEmailSent != null)
                                {
                                    DateTime bomLastSendDate = (DateTime)checkLastEmailSent.LastEmailSentDate;
                                    var totalSentHours = (today - bomLastSendDate.Date).TotalHours;
                                    if (totalSentHours >= 12)
                                    {
                                        if (sendToUserType == "HOD")
                                        {
                                            var pr = _context.Prs.Where(x => x.Id == bom.PRId).FirstOrDefault();
                                            var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                                            var prDepartment = prRoot.data.header.Select(x => x.department).FirstOrDefault();

                                            if (prDepartment == "BD")
                                            {
                                                bomListBD.Add(bom);
                                            }
                                            else if (prDepartment == "FE")
                                            {
                                                bomListFE.Add(bom);
                                            }
                                            else if (prDepartment == "FAS")
                                            {
                                                bomListFAS.Add(bom);
                                            }
                                            else if (prDepartment == "OE")
                                            {
                                                bomListOE.Add(bom);
                                            }
                                            else if (prDepartment == "R&D")
                                            {
                                                bomListRnD.Add(bom);
                                            }
                                            else if (prDepartment == "SI")
                                            {
                                                bomListSI.Add(bom);
                                            }
                                        }
                                        else
                                        {
                                            bomList.Add(bom);
                                        }
                                    }
                                }
                                else
                                {
                                    if (sendToUserType == "HOD")
                                    {
                                        var pr = _context.Prs.Where(x => x.Id == bom.PRId).FirstOrDefault();
                                        var prRoot = JsonConvert.DeserializeObject<PRRoot>(pr.DataResponse);
                                        var prDepartment = prRoot.data.header.Select(x => x.department).FirstOrDefault();

                                        if (prDepartment == "BD")
                                        {
                                            bomListBD.Add(bom);
                                        }
                                        else if (prDepartment == "FE")
                                        {
                                            bomListFE.Add(bom);
                                        }
                                        else if (prDepartment == "FAS")
                                        {
                                            bomListFAS.Add(bom);
                                        }
                                        else if (prDepartment == "OE")
                                        {
                                            bomListOE.Add(bom);
                                        }
                                        else if (prDepartment == "R&D")
                                        {
                                            bomListRnD.Add(bom);
                                        }
                                        else if (prDepartment == "SI")
                                        {
                                            bomListSI.Add(bom);
                                        }
                                    }
                                    else
                                    {
                                        bomList.Add(bom);
                                    }
                                }
                            }
                        }

                        if (bomList.Any())
                        {
                            await SendPRPendingApprovalEmail(bomList.OrderBy(x => x.ProjectId).ToList(), sendToUserType);
                        }

                        if (bomListBD.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListBD.OrderBy(x => x.ProjectId).ToList(), "HOD", "BD");
                        }
                        if (bomListFE.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListFE.OrderBy(x => x.ProjectId).ToList(), "HOD", "FE");
                        }
                        if (bomListFAS.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListFAS.OrderBy(x => x.ProjectId).ToList(), "HOD", "FAS");
                        }
                        if (bomListOE.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListOE.OrderBy(x => x.ProjectId).ToList(), "HOD", "OE");
                        }
                        if (bomListRnD.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListRnD.OrderBy(x => x.ProjectId).ToList(), "HOD", "R&D");
                        }
                        if (bomListSI.Any())
                        {
                            await SendPRPendingApprovalEmail(bomListSI.OrderBy(x => x.ProjectId).ToList(), "HOD", "SI");
                        }

                    }

                } //end loop
            }//end try
            catch (Exception e)
            {
                throw e;
            }
        }


        public async Task PRApprovedNotification()
        {
            try
            {
                var getBoms = _context.Boms.Where(x => x.Status == "PR Approved").ToList();
                var bomList = new List<Bom>();
                foreach (var bom in getBoms)
                {
                    DateTime bomLastUpdateDate = (DateTime)bom.LastUpdatedDate;
                    DateTime today = DateTime.Now.Date;
                    var totalhours = (today - bomLastUpdateDate.Date).TotalHours;
                    //var totalDays = (today - bomLastUpdateDate.Date).TotalHours;
                    if (totalhours >= 12)
                    {
                        //Comment this part if wanna test..//
                        var checkLastEmailSent = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                        if (checkLastEmailSent != null)
                        {
                            DateTime bomLastSendDate = (DateTime)checkLastEmailSent.LastEmailSentDate;
                            //var totalSentDays = (today - bomLastSendDate.Date).TotalDays;
                            var totalSentHours = (today - bomLastSendDate.Date).TotalHours;
                            if (totalSentHours >= 12)
                            {
                                bomList.Add(bom);
                            }
                        }
                        else
                        {
                            bomList.Add(bom);
                        }
                    }
                }

                if (bomList.Any())
                {
                    await SendPRApprovedEmail(bomList.OrderBy(x => x.ProjectId).ToList());
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }



        public async Task BOMPendingCompletedNotification()
        {
            try
            {
                var getBoms = _context.Boms.Where(x => x.Status == "Item Received").ToList();
                var bomList = new List<Bom>();
                foreach (var bom in getBoms)
                {
                    DateTime bomLastUpdateDate = (DateTime)bom.LastUpdatedDate;
                    DateTime today = DateTime.Now.Date;
                    var totaldays = (today - bomLastUpdateDate.Date).TotalDays;
                    //var totalDays = (today - bomLastUpdateDate.Date).TotalHours;
                    if (totaldays >= 7)
                    {
                        //Comment this part if wanna test..//
                        var checkLastEmailSent = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                        if (checkLastEmailSent != null)
                        {
                            DateTime bomLastSendDate = (DateTime)checkLastEmailSent.LastEmailSentDate;
                            var totalSentDays = (today - bomLastSendDate.Date).TotalDays;
                            if (totalSentDays >= 7)
                            {
                                bomList.Add(bom);
                            }
                        }
                        else
                        {
                            bomList.Add(bom);
                        }
                    }
                }

                if (bomList.Any())
                {
                    await SendBOMPendingCompletedEmail(bomList.OrderBy(x => x.ProjectId).ToList());
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }



        public async Task SendPendingRFQEmail(List<Bom> bomList)
        {
            try
            {
                var employeeInfoList = _context.EmployeeInfos.ToList();
                string subject = "RFQ Pending for Review";
                string html = RFQEmailContent(bomList, "review", subject);

                //SEND TO TOP MANAGEMENT
                var targetUserType = new List<string>()
                {
                    "Buyer"
                };

                var emailList = employeeInfoList.Where(x => !string.IsNullOrEmpty(x.Email) && targetUserType.Contains(x.UserType) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                //var testemailList = new List<string>() { "zoseph995@gmail.com" }; //Dev test purpose
                if(emailList.Count > 0)
                {
                    await _emailService.Send(emailList, subject, html);
                }

                //UPDATE LAST EMAIL SENT DATE
                var bomIds = bomList.Select(x => new { x.Id, x.LastUpdatedDate }).ToList();
                foreach (var bom in bomIds)
                {
                    var updateBoms = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                    if (updateBoms == null)
                    {
                        var model = new BomsendEmail();
                        model.BomId = bom.Id;
                        model.BomlastUpdatedDate = bom.LastUpdatedDate;
                        model.CreatedDate = DateTime.Now;
                        model.CreatedBy = "Quartz Scheduler";
                        model.LastEmailSentDate = DateTime.Now;

                        _context.BomSendEmail.Add(model);
                    }
                    else
                    {
                        updateBoms.LastEmailSentDate = DateTime.Now;
                        _context.BomSendEmail.Update(updateBoms);
                    }
                    _context.SaveChanges();
                }
                //<end>UPDATE LAST EMAIL SENT DATE
            }
            catch (Exception ex)
            {
                throw;
            }
        }


        public async Task SendPRPendingApprovalEmail(List<Bom> bomList, string userType, string dept = "")
        {
            try
            {
                var employeeInfoList = _context.EmployeeInfos
                    .Include(x => x.EmpDepartment)
                    .ToList();
                string subject = "PR Pending for Approval";
                string html = PRPendingApprovalEmailContent(bomList, subject);
                var emailList = new List<string>();

                if (!String.IsNullOrEmpty(dept))
                {
                    emailList = employeeInfoList.Where(x => !string.IsNullOrEmpty(x.Email) && x.UserType == userType && x.EmpDepartment.Name == dept && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                }
                else
                {
                    emailList = employeeInfoList.Where(x => !string.IsNullOrEmpty(x.Email) && x.UserType == userType && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                }
                //var testemailList = new List<string>() { "zoseph995@gmail.com" }; //Dev test purpose
                if(emailList.Count > 0)
                {
                    await _emailService.Send(emailList, subject, html);
                }

                //UPDATE LAST EMAIL SENT DATE
                var bomIds = bomList.Select(x => new { x.Id, x.LastUpdatedDate }).ToList();
                foreach (var bom in bomIds)
                {
                    var updateBoms = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                    if (updateBoms == null)
                    {
                        var model = new BomsendEmail();
                        model.BomId = bom.Id;
                        model.BomlastUpdatedDate = bom.LastUpdatedDate;
                        model.CreatedDate = DateTime.Now;
                        model.CreatedBy = "Quartz Scheduler";
                        model.LastEmailSentDate = DateTime.Now;

                        _context.BomSendEmail.Add(model);
                    }
                    else
                    {
                        updateBoms.LastEmailSentDate = DateTime.Now;
                        _context.BomSendEmail.Update(updateBoms);
                    }
                    _context.SaveChanges();
                }
                //<end>UPDATE LAST EMAIL SENT DATE
            }
            catch (Exception ex)
            {
                throw;
            }
        }


        public async Task SendPRApprovedEmail(List<Bom> bomList)
        {
            try
            {
                var employeeInfoList = _context.EmployeeInfos.ToList();
                string subject = "PR Approved. Pending PO Update";
                string html = PRApprovedEmailContent(bomList, subject);

                //SEND TO TOP MANAGEMENT
                var targetUserType = new List<string>()
                {
                    "Purchaser"
                };

                var emailList = employeeInfoList.Where(x => !string.IsNullOrEmpty(x.Email) && targetUserType.Contains(x.UserType) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                //var testemailList = new List<string>() { "zoseph995@gmail.com" }; //Dev test purpose
                if(emailList.Count > 0 )
                {
                    await _emailService.Send(emailList, subject, html);
                }
                //<end>SEND TO TOP MANAGEMENT




                //UPDATE LAST EMAIL SENT DATE
                var bomIds = bomList.Select(x => new { x.Id, x.LastUpdatedDate }).ToList();
                foreach (var bom in bomIds)
                {
                    var updateBoms = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                    if (updateBoms == null)
                    {
                        var model = new BomsendEmail();
                        model.BomId = bom.Id;
                        model.BomlastUpdatedDate = bom.LastUpdatedDate;
                        model.CreatedDate = DateTime.Now;
                        model.CreatedBy = "Quartz Scheduler";
                        model.LastEmailSentDate = DateTime.Now;

                        _context.BomSendEmail.Add(model);
                    }
                    else
                    {
                        updateBoms.LastEmailSentDate = DateTime.Now;
                        _context.BomSendEmail.Update(updateBoms);
                    }
                    _context.SaveChanges();
                }
                //<end>UPDATE LAST EMAIL SENT DATE
            }
            catch (Exception ex)
            {
                throw;
            }
        }


        public async Task SendBOMPendingCompletedEmail(List<Bom> bomList)
        {
            try
            {
                var employeeInfoList = _context.EmployeeInfos.ToList();
                string subject = "Received Item Still Not Completed";
                string html = BOMNotCompletedEmailContent(bomList, subject);

                //SEND TO TOP MANAGEMENT
                var targetUserType = new List<string>()
                {
                    "Finance"
                };

                var emailList = employeeInfoList.Where(x => !string.IsNullOrEmpty(x.Email) && targetUserType.Contains(x.UserType) && x.IsNotificationOn).Select(x => x.Email).Distinct().ToList();
                //var testemailList = new List<string>() { "zoseph995@gmail.com" }; //Dev test purpose
                if(emailList.Count > 0)
                {
                    await _emailService.Send(emailList, subject, html);
                }

                //UPDATE LAST EMAIL SENT DATE
                var bomIds = bomList.Select(x => new { x.Id, x.LastUpdatedDate }).ToList();
                foreach (var bom in bomIds)
                {
                    var updateBoms = _context.BomSendEmail.Where(x => x.BomId == bom.Id).FirstOrDefault();
                    if (updateBoms == null)
                    {
                        var model = new BomsendEmail();
                        model.BomId = bom.Id;
                        model.BomlastUpdatedDate = bom.LastUpdatedDate;
                        model.CreatedDate = DateTime.Now;
                        model.CreatedBy = "Quartz Scheduler";
                        model.LastEmailSentDate = DateTime.Now;

                        _context.BomSendEmail.Add(model);
                    }
                    else
                    {
                        updateBoms.LastEmailSentDate = DateTime.Now;
                        _context.BomSendEmail.Update(updateBoms);
                    }
                    _context.SaveChanges();
                }
                //<end>UPDATE LAST EMAIL SENT DATE
            }
            catch (Exception ex)
            {
                throw;
            }
        }




        public string RFQEmailContent(List<Bom> bomList, string pendingFor, string title)
        {
            var sc = new ServerContext(_server);
            //var url = sc.BaseUrl();
            var url = sc.GetServerAddress();

            string message = $@"<p>Dear user,</p>
                                <p> Please find below for RFQ(s) that pending for {pendingFor}.</p>";

            string tblHeader = @"<table class='table table-bordered' style='border: 1px solid black; text-align:center;font-size:14px;'>
                                <thead>
                                <tr>
                                    <th style='border: 1px solid black'>Project Name</th>
                                    <th style='border: 1px solid black'>Item Category</th>
                                    <th style='border: 1px solid black'>Module</th>
                                    <th style='border: 1px solid black'>Part Number</th>
                                    <th style='border: 1px solid black'>Brand</th>
                                    <th style='border: 1px solid black'>Product Specification</th>
                                    <th style='border: 1px solid black;min-width: 150px;'>Status</th>
                                    <th style='border: 1px solid black'>Action</th>
                                </tr>
                                </thead>";

            string tblContent = "";
            string logo = @" <tr>
                                 <td align = ""center"" valign = ""center"" style = ""text - align:center; "">
                                    <img alt = ""Logo"" src = ""https://mymagic-central.s3.amazonaws.com/uploads/organization/thumbnail/21d15c5bc1086d561af1936c53deefcbfdf72fb2.adaptiveResize.320x320.png"" />
                                 </td>
                             </tr> ";
            var bomIds = bomList.Select(x => x.Id).ToList();
            //var rfqs = _context.Rfqs.Where(x => bomIds.Contains((int)x.Bomid)).ToList();
            var rfqs = bomList.Select(x => x.RFQ).ToList();
            foreach (var bom in bomList)
            {
                var rfqId = bom.RFQId;
                var projectName = _context.Projects.Where(x => x.Id == bom.ProjectId).Select(x => x.Name).FirstOrDefault();
                tblContent += "<tr>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + projectName + "</p>" +
                                    "</td>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.ItemCategory + "</p>" +
                                    "</td>" +

                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Module + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartNumber + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Brand + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartDesc + "</p>" +
                                    "</td>" +
                                     "<td style='border:1px solid black'>" +
                                        "<p>" + bom.Status + "</p>" +
                                    "</td>" +

                                    "<td style='border:1px solid black;'>" +
                                        "<a style='text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;margin-left:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle' href='" + url + "RFQ/Edit/" + rfqId + "'>View</a>" +
                                    "</td>" +
                                 "</tr>";
            }

            string tblBody = @"<tbody>" + tblContent + "</tbody></table>";



            var content = message + tblHeader + tblBody;

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
                                                {content}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";
            return html;
        }


        public string PRApprovedEmailContent(List<Bom> bomList, string title)
        {
            var sc = new ServerContext(_server);
            //var url = sc.BaseUrl();
            var url = sc.GetServerAddress();

            string message = $@"<p>Dear user,</p>
                                <p> Please note that below PR(s) have already approved. Kindly proceed to update the PO.</p>";

            string tblHeader = @"<table class='table table-bordered' style='border: 1px solid black; text-align:center;font-size:14px;'>
                                <thead>
                                <tr>
                                    <th style='border: 1px solid black'>Project Name</th>
                                    <th style='border: 1px solid black'>Item Category</th>
                                    <th style='border: 1px solid black'>Module</th>
                                    <th style='border: 1px solid black'>Part Number</th>
                                    <th style='border: 1px solid black'>Brand</th>
                                    <th style='border: 1px solid black'>Product Specification</th>
                                    <th style='border: 1px solid black;min-width: 150px;'>Status</th>
                                    <th style='border: 1px solid black'>Action</th>
                                </tr>
                                </thead>";

            string tblContent = "";
            string logo = @" <tr>
                                 <td align = ""center"" valign = ""center"" style = ""text - align:center; "">
                                    <img alt = ""Logo"" src = ""https://mymagic-central.s3.amazonaws.com/uploads/organization/thumbnail/21d15c5bc1086d561af1936c53deefcbfdf72fb2.adaptiveResize.320x320.png"" />
                                 </td>
                             </tr> ";
            var bomIds = bomList.Select(x => x.Id).ToList();
            //var rfqs = _context.Rfqs.Where(x => bomIds.Contains((int)x.Bomid)).ToList();
            //var rfqs = bomList.Select(x => x.RFQ).ToList();
            foreach (var bom in bomList)
            {
                //var rfqId = rfqs.Where(x => x.Bomid == bom.Id).Select(x => x.Id).FirstOrDefault();
                var PrId = bom.PRId;
                var projectName = _context.Projects.Where(x => x.Id == bom.ProjectId).Select(x => x.Name).FirstOrDefault();
                tblContent += "<tr>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + projectName + "</p>" +
                                    "</td>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.ItemCategory + "</p>" +
                                    "</td>" +

                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Module + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartNumber + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Brand + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartDesc + "</p>" +
                                    "</td>" +

                                     "<td style='border:1px solid black'>" +
                                        "<p>" + bom.Status + "</p>" +
                                    "</td>" +

                                    "<td style='border:1px solid black;'>" +
                                        "<a style='text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;margin-left:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle' href='" + url + "PurchaseRequisition/Edit/" + PrId + "'>View</a>" +
                                    "</td>" +
                                 "</tr>";
            }

            string tblBody = @"<tbody>" + tblContent + "</tbody></table>";



            var content = message + tblHeader + tblBody;

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
                                                {content}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";
            return html;
        }


        public string PRPendingApprovalEmailContent(List<Bom> bomList, string title)
        {
            var sc = new ServerContext(_server);
            //var url = sc.BaseUrl();
            var url = sc.GetServerAddress();

            string message = $@"<p>Dear user,</p>
                                <p> Please find below for PR(s) that pending for approval.</p>";

            string tblHeader = @"<table class='table table-bordered' style='border: 1px solid black; text-align:center;font-size:14px;'>
                                <thead>
                                <tr>
                                    <th style='border: 1px solid black'>Project Name</th>
                                    <th style='border: 1px solid black'>Item Category</th>
                                    <th style='border: 1px solid black'>Module</th>
                                    <th style='border: 1px solid black'>Part Number</th>
                                    <th style='border: 1px solid black'>Brand</th>
                                    <th style='border: 1px solid black'>Product Specification</th>
                                   
                                    <th style='border: 1px solid black;min-width: 150px;'>Status</th>
                                    <th style='border: 1px solid black'>Action</th>
                                </tr>
                                </thead>";

            string tblContent = "";
            string logo = @" <tr>
                                 <td align = ""center"" valign = ""center"" style = ""text - align:center; "">
                                    <img alt = ""Logo"" src = ""https://mymagic-central.s3.amazonaws.com/uploads/organization/thumbnail/21d15c5bc1086d561af1936c53deefcbfdf72fb2.adaptiveResize.320x320.png"" />
                                 </td>
                             </tr> ";
            var bomIds = bomList.Select(x => x.Id).ToList();
            //var rfqs = _context.Rfqs.Where(x => bomIds.Contains((int)x.Bomid)).ToList();
            //var rfqs = bomList.Select(x => x.RFQ).ToList();
            foreach (var bom in bomList)
            {
                //var rfqId = rfqs.Where(x => x.Bomid == bom.Id).Select(x => x.Id).FirstOrDefault();
                var PrId = bom.PRId;
                var projectName = _context.Projects.Where(x => x.Id == bom.ProjectId).Select(x => x.Name).FirstOrDefault();
                tblContent += "<tr>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + projectName + "</p>" +
                                    "</td>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.ItemCategory + "</p>" +
                                    "</td>" +

                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Module + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartNumber + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Brand + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartDesc + "</p>" +
                                    "</td>" +
                                     "<td style='border:1px solid black'>" +
                                        "<p>" + bom.Status + "</p>" +
                                    "</td>" +

                                    "<td style='border:1px solid black;'>" +
                                        "<a style='text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;margin-left:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle' href='" + url + "PurchaseRequisition/Edit/" + PrId + "'>View</a>" +
                                    "</td>" +
                                 "</tr>";
            }

            string tblBody = @"<tbody>" + tblContent + "</tbody></table>";



            var content = message + tblHeader + tblBody;

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
                                                {content}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";
            return html;
        }



        public string BOMNotCompletedEmailContent(List<Bom> bomList, string title)
        {
            var sc = new ServerContext(_server);
            //var url = sc.BaseUrl();
            var url = sc.GetServerAddress();

            string message = $@"<p>Dear user,</p>
                                <p> Please note that below item(s) are received but still haven't completed yet.</p>";

            string tblHeader = @"<table class='table table-bordered' style='border: 1px solid black; text-align:center;font-size:14px;'>
                                <thead>
                                <tr>
                                    <th style='border: 1px solid black'>Project Name</th>
                                    <th style='border: 1px solid black'>Item Category</th>
                                    <th style='border: 1px solid black'>Module</th>
                                    <th style='border: 1px solid black'>Part Number</th>
                                    <th style='border: 1px solid black'>Brand</th>
                                    <th style='border: 1px solid black'>Product Specification</th>
                                   
                                    <th style='border: 1px solid black;min-width: 150px;'>Status</th>
                                    <th style='border: 1px solid black'>Action</th>
                                </tr>
                                </thead>";

            string tblContent = "";
            string logo = @" <tr>
                                 <td align = ""center"" valign = ""center"" style = ""text - align:center; "">
                                    <img alt = ""Logo"" src = ""https://mymagic-central.s3.amazonaws.com/uploads/organization/thumbnail/21d15c5bc1086d561af1936c53deefcbfdf72fb2.adaptiveResize.320x320.png"" />
                                 </td>
                             </tr> ";
            foreach (var bom in bomList)
            {
                var projectName = _context.Projects.Where(x => x.Id == bom.ProjectId).Select(x => x.Name).FirstOrDefault();
                tblContent += "<tr>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + projectName + "</p>" +
                                    "</td>" +
                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.ItemCategory + "</p>" +
                                    "</td>" +

                                    "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Module + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartNumber + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.Brand + "</p>" +
                                    "</td>" +

                                     "<td style='border: 1px solid black'>" +
                                        "<p>" + bom.PartDesc + "</p>" +
                                    "</td>" +
                                     "<td style='border:1px solid black'>" +
                                        "<p>" + bom.Status + "</p>" +
                                    "</td>" +

                                    "<td style='border:1px solid black;'>" +
                                        "<a style='text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;margin-left:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle' href='" + url + "ReceivingTracker/Index'>View</a>" +
                                    "</td>" +
                                 "</tr>";
            }

            string tblBody = @"<tbody>" + tblContent + "</tbody></table>";



            var content = message + tblHeader + tblBody;

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
							                        <h3 style=""text-align:center;"">{title} </h3>
						                        </div>
                                                {content}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";
            return html;
        }
    }
}
