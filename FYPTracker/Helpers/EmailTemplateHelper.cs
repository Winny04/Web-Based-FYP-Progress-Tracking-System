using System;

namespace FYPTracker.Helpers
{
    public static class EmailTemplateHelper
    {
        public static string GetStandardizedTemplate(string title, string message, string? ctaText = null, string? ctaUrl = null)
        {
            string ctaHtml = "";
            if (!string.IsNullOrEmpty(ctaText) && !string.IsNullOrEmpty(ctaUrl))
            {
                ctaHtml = $@"<div style=""text-align:center; padding-bottom: 20px;""><a href='{ctaUrl}' style=""text-decoration:none;display:inline-block;padding:10px 20px;color:white;background-color:#0d9488;border-radius:6px;font-weight:bold;"">{ctaText}</a></div><br/>";
            }

            return $@"<div style=""font-family:Arial,Helvetica,sans-serif; line-height: 1.5; font-size: 15px; color: #2F3044; background-color:#edf2f7; padding:40px 20px;"">
                <table align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""max-width:600px; background-color:#ffffff; border-radius:6px; padding:40px;"">
                    <tr><td align=""center"" style=""padding-bottom: 20px;""><span style=""font-size: 24px; font-weight: bold; color: #0d9488;"">FYP Tracking System</span></td></tr>
                    <tr><td align=""left""><h3 style=""text-align:center; padding-bottom: 20px;"">{title}</h3>{message}<br/>{ctaHtml}<p>Best Regards,<br/>FYP Tracking System</p></td></tr>
                </table></div>";
        }
    }
}
