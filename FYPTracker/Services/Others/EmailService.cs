using FYPTracker.Configuration;
using FYPTracker.Services.Others.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace FYPTracker.Services.Others
{
    public class EmailService : IEmailService
    {
        private readonly SmtpSettings smtpSettings;
        private readonly IWebHostEnvironment _env;

        public EmailService(SmtpSettings smtpSettings, IWebHostEnvironment env)
        {
            this.smtpSettings = smtpSettings;
            _env = env;
        }

        public void Send(string to, string subject, string html,string? filePath = null, string from = null, string? cc = null)
        {
            if (smtpSettings.Offline) return;

            try
            {
                var email = new MimeMessage();

                email.From.Add(MailboxAddress.Parse(from ?? smtpSettings.EmailFrom));

                var multiPart = new Multipart("mixed");

                foreach (var addr in to.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    email.To.Add(MailboxAddress.Parse(addr.Trim()));

                if (!string.IsNullOrEmpty(cc))
                    email.Cc.Add(MailboxAddress.Parse(cc.Trim()));

                email.Subject = subject;
                var bodyPart = new TextPart(TextFormat.Html) { Text = html };
                multiPart.Add(bodyPart);

                if(!string.IsNullOrEmpty(filePath))
                {
                    var attachment = new MimePart("application", "pdf")
                    {
                        Content = new MimeContent(File.OpenRead(filePath)),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(filePath)
                    };
                    multiPart.Add(attachment);
                }

                email.Body = multiPart;

                // send email
                using (var smtp = new SmtpSubscribeSingleton())
                {
                    smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                    //smtp.CheckCertificateRevocation = false;
                    smtp.Connect(smtpSettings.SmtpHost, smtpSettings.SmtpPort, SecureSocketOptions.SslOnConnect);
                    smtp.Authenticate(smtpSettings.SmtpUser, smtpSettings.SmtpPass);
                    smtp.Send(email);
                    smtp.Disconnect(true);
                    smtp.Dispose();
                }
            }
            catch
            {

            }
        }

        public async Task Send(List<string> to, string subject, string html, string? filePath = null, string from = null)
        {
            // create message
            if (smtpSettings.Offline) return;

            try
            {
                var email = new MimeMessage();

                email.From.Add(MailboxAddress.Parse(from ?? smtpSettings.EmailFrom));
                foreach (var address in to)
                {
                    email.To.Add(MailboxAddress.Parse(address));
                }

                var multiPart = new Multipart("mixed");

                email.Subject = subject;
                var bodyPart = new TextPart(TextFormat.Html) { Text = html };
                multiPart.Add(bodyPart);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var attachment = new MimePart("application", "pdf")
                    {
                        Content = new MimeContent(File.OpenRead(filePath)),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(filePath)
                    };
                    multiPart.Add(attachment);
                }

                email.Body = multiPart;

                // send email
                using (var smtp = new SmtpClient())
                {
                    try
                    {
                        smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                        //smtp.CheckCertificateRevocation = false;
                        await smtp.ConnectAsync(smtpSettings.SmtpHost, smtpSettings.SmtpPort, SecureSocketOptions.SslOnConnect);
                        await smtp.AuthenticateAsync(smtpSettings.SmtpUser, smtpSettings.SmtpPass);
                        await smtp.SendAsync(email);
                        await smtp.DisconnectAsync(true);
                        smtp.Dispose();
                    }
                    catch (SmtpCommandException ex)
                    {
                        Console.WriteLine("Error trying to connect: {0}", ex.Message);
                        Console.WriteLine("\tStatusCode: {0}", ex.StatusCode);
                        return;
                    }
                    catch (SmtpProtocolException ex)
                    {
                        Console.WriteLine("Protocol error while trying to connect: {0}", ex.Message);
                        return;
                    }
                   
                }
            }
            catch (Exception ex)
            {
            }
        }
    }

}
