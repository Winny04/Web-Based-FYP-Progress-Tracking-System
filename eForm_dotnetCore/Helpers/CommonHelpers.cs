using eForm_dotnetCore.Common;
using eForm_dotnetCore.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.RegularExpressions;

namespace eForm_dotnetCore.Helpers
{
    public class CommonHelpers
    {
        private static IHttpContextAccessor httpContextAccessor;
        private static IWebHostEnvironment _env;
        private static readonly List<string> listStatus = new List<string>()
        {
            "New",
            "RFQ Sent",
            "RFQ Updated",
            "RFQ Reject",
            "RFQ Approved",
            "PR Sent",
            "PR Reject",
            "PR Approved",
            "PO Revise",
            "PO Reject",
            "PO Approved",
            "ETA Updated",
            "Partial Received",
            "Item Received",
            "Item Issued Out",
            "Completed"
        };
        public static void SetHttpContextAccessor(
            IHttpContextAccessor accessor,
            IWebHostEnvironment env)
        {
            httpContextAccessor = accessor;
            _env = env;
        }


        public static string RandomString(int size)
        {
            Random random = new Random((int)DateTime.Now.Ticks);
            StringBuilder builder = new StringBuilder();
            char ch;
            for (int i = 0; i < size; i++)
            {
                ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * random.NextDouble() + 65)));
                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static (string, string) MailTemplateGenerator(string type, BOMStatus action, string redirect,string reason = "")
        {
            var httpContext = httpContextAccessor.HttpContext;

            var title = type + " " + action.ToString().ConvertEnum();
            var extraMessage = !string.IsNullOrEmpty(reason) ? "because of " + reason : "";
            string message = $@"<p>Dear Users,</p><br />
                                <p>Task Name: {title}</p>
                                <p>The {type} has been {action} by {httpContext.User.Identity.Name} {extraMessage}</p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static string ApprovalRequireMailTemp(string role, string redirect, string prName)
        {
            var httpContext = httpContextAccessor.HttpContext;

            var title = prName + " is pending for approval";
            string message = $@"<p>Dear {role},</p><br />
                                <p>Task Name: {title}</p>
                                <p>Please find below for PR that pending for approval</p>
                                <p>Please click on button below to view the PR:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return html;
        }

        public static string renamePR(string name,int version)
        {
            if(version > 0) return name+ "_R" + version;

            return name;
        }

        public static (string, string) ProjectStatusMailTemplateGenerator(string type, string action, string redirect, string code)
        {
            var httpContext = httpContextAccessor.HttpContext;

            var title = "The " + type + "status has been change to " + action;
            string message = $@"<p>Dear Users,</p><br />
                                <p>Task Name: {title}</p>
                                <p>The {type} Code {code} status has been change to {action}.</p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static (string, string) ProjectMailTemplateGenerator(string type, string action, string redirect, string code)
        {
            var httpContext = httpContextAccessor.HttpContext;

            var title = type + " " + action;
            string message = $@"<p>Dear Users,</p><br />
                                <p>Task Name: {title}</p>
                                <p>The {type} added in the system by {httpContext.User.Identity.Name}. Project Code: {code}.</p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static (string, string) AddOrDeletePorjectPICTemplate(string title, string action, string projectName, string redirect)
        {
            var httpContext = httpContextAccessor.HttpContext;
            string message = "";
            if (action == "ADDED")
            {
                message = $@"<p>Dear User,</p><br />
                                <p>Kindly note that you has been <b>{action}</b> as a PIC of the project: <b>{projectName}</b> by {httpContext.User.Identity.Name} </p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            }
            else
            {
                message = $@"<p>Dear User,</p><br />
                                <p>Kindly note that you has been <b>{action}</b> from the project: <b>{projectName}</b> by {httpContext.User.Identity.Name} as a PIC. </p>
                               ";
            }
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static (string, string) PICLeaderAddOrDeletePIC(string title, string leader, string projectName, string redirect)
        {
            var httpContext = httpContextAccessor.HttpContext;
            string message = "";
            message = $@"<p>Dear User,</p><br />
                                <p>Kindly note that the PIC of the Project <b>{projectName}</b> have been <b>modified>/b> the PIC Leader {httpContext.User.Identity.Name} </p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
					        </div>";


            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static (string, string) ItemIssuedOutEmailTemplate(string item, string action, string issuedPIC, string redirect)
        {
            var httpContext = httpContextAccessor.HttpContext;

            var title = "Item Issued Out";
            string message = $@"<p>Dear Users,</p><br />
                                <p>Task Name: {title}</p>
                                <p>The item No. {item} has been <b>{action}</b> by {issuedPIC}</p>
                                <p>Please click on button below to view the task:</p>
						        <div style=""padding-bottom: 40px; text-align:center;"">
							        <a href='https://{httpContext.Request.Host}{redirect}' style=""text-decoration:none;display:inline-block;text-align:center;padding:0.75575rem 1.3rem;font-size:0.925rem;line-height:1.5;border-radius:0.35rem;color:#ffffff;background-color:#009EF7;border:0px;margin-right:0.75rem!important;font-weight:600!important;outline:none!important;vertical-align:middle"">Click Here</a>
						        </div>";
            string html = $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-weight: normal; font-size: 15px; color: #2F3044; min-height: 100%; margin:0; padding:0; width:100%; background-color:#edf2f7;padding-bottom:40px"">
	                        <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""border-collapse:collapse;margin:0 auto; padding:0; max-width:600px"">
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
                                                {message}
                                                </br>
					                        </div>
				                        </td>
			                        </tr>
		                        </tbody>
	                        </table>
                        </div>";

            return (message, html);
        }

        public static bool availableBaseStatus(string requireStatus, string status)
        {
            int index1 = listStatus.IndexOf(requireStatus);
            int index2 = listStatus.IndexOf(status);

            return index2 >= index1;
        }

        public static string GetPrevStatus(string status)
        {
            int index = listStatus.IndexOf(status);

            if (index < 0)
            {
                return status;
            }

            if(index > 0)
            {
                index -= 1;
                var prevStatus = listStatus[index];
                return prevStatus;
            }

            return listStatus[index];
        }

        public static string ConvertColumnName(string name)
        {
            string newName = "";
            if (!String.IsNullOrEmpty(name))
            {
                if (name.ToLower() == "code")
                {
                    newName = "Project Code";
                }
                else if (name.ToLower().Replace(" ", string.Empty) == "ponumber")
                {
                    newName = "Project PO";
                }
                else if (name.ToLower() == "requester")
                {
                    newName = "Request By";
                }
                else
                {
                    newName = name;
                }
            }

            return newName;
        }

        public static bool isPics(Project project)
        {
            if (project != null && !string.IsNullOrEmpty(project.PICS))
            {
                if (httpContextAccessor.HttpContext.GetUserType() == "SuperAdminAssistant" || httpContextAccessor.HttpContext.GetUserType() == "SuperAdmin")
                {
                    return true;
                }

                var listIds = new List<int>();
                var pics = !string.IsNullOrEmpty(project.PICS) ? project.PICS.Split(',').ToArray(): null;
                if(pics != null)
                {
                    foreach(var pic in pics)
                    {
                        if(int.TryParse(pic,out var empId)) listIds.Add(empId); 
                    }
                }

                var userId = int.Parse(httpContextAccessor.HttpContext.GetUserId());
                if (listIds.Contains(userId)) return true;
            }

            return false;
        }

        public static bool isImage(string type)
        {
            var imageExtension = new List<string> { ".png", ".jpeg", ".jpg" };

            return imageExtension.Contains(type);
        }

        public static string generateUID()
        {
            var uid = Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
            return uid;
        }

        public static bool checkIsBuyerOrPurchaser()
        {
            var userType = httpContextAccessor.HttpContext.GetUserType();
            if (userType == "Buyer" 
                || userType == "Purchaser")
            {
                return true;
            }
            return false;
        }
        public static List<string> LevelApprover(double price)
        {
            var approvers = new List<string>();
            if (price <= 10000)
            {
                approvers.Add("HOD");
                return approvers;
            }
            else if (price > 10000 && price <= 30000)
            {
                approvers.Add("HOD");
                approvers.Add("GFC");
            }
            else if (price > 30000)
            {
                approvers.Add("HOD");
                approvers.Add("GFC");
                approvers.Add("CEO");
            }

            return approvers;
        }
    }
}
