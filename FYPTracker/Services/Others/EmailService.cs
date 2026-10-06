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

        /// <summary>
        /// Returns true if development redirect is active.
        /// Logs a clear error and returns false if DevRedirectEmail is configured
        /// but invalid/empty, preventing accidental sends to real addresses.
        /// </summary>
        private bool TryGetDevRedirect(out string redirectTo)
        {
            redirectTo = null!;

            if (!_env.IsDevelopment())
                return false;

            if (string.IsNullOrWhiteSpace(smtpSettings.DevRedirectEmail))
                return false; // Not configured → send normally (Production-like behavior)

            var candidate = smtpSettings.DevRedirectEmail.Trim();
            if (!candidate.Contains('@') || candidate.Length < 5)
            {
                // Invalid redirect email — fail loudly in development rather than
                // accidentally sending to a real institutional address
                Console.WriteLine($"[EmailService] ⚠ DEV REDIRECT BLOCKED: DevRedirectEmail \"{candidate}\" is invalid. Email will NOT be sent.");
                redirectTo = null!;
                return false;
            }

            redirectTo = candidate;
            return true;
        }

        public void Send(string to, string subject, string html, string? filePath = null, string from = null, string? cc = null)
        {
            if (smtpSettings.Offline) return;

            try
            {
                var email = new MimeMessage();

                email.From.Add(MailboxAddress.Parse(from ?? smtpSettings.EmailFrom));

                var multiPart = new Multipart("mixed");

                // ── Development redirect ──────────────────────────────────
                string actualRecipient;
                if (TryGetDevRedirect(out var devEmail))
                {
                    actualRecipient = devEmail;
                    email.To.Add(MailboxAddress.Parse(devEmail));

                    // Prefix subject so the developer knows who the real target was
                    subject = $"[DEV → {to}] {subject}";

                    // Log safe info (no tokens/passwords)
                    Console.WriteLine($"[EmailService] 📧 DEV REDIRECT: Original=\"{to}\", Redirected=\"{devEmail}\", Subject=\"{subject}\"");
                }
                else
                {
                    actualRecipient = to;
                    foreach (var addr in to.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        email.To.Add(MailboxAddress.Parse(addr.Trim()));

                    if (!string.IsNullOrEmpty(cc))
                        email.Cc.Add(MailboxAddress.Parse(cc.Trim()));
                }
                // ── End redirect ──────────────────────────────────────────

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

                if (_env.IsDevelopment())
                    Console.WriteLine($"[EmailService] ✅ Email sent successfully to \"{actualRecipient}\".");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] ❌ Email sending failed to \"{to}\": {ex.Message}");
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

                // ── Development redirect ──────────────────────────────────
                string actualRecipient;
                string originalRecipients = string.Join("; ", to);

                if (TryGetDevRedirect(out var devEmail))
                {
                    actualRecipient = devEmail;
                    email.To.Add(MailboxAddress.Parse(devEmail));

                    subject = $"[DEV → {originalRecipients}] {subject}";

                    Console.WriteLine($"[EmailService] 📧 DEV REDIRECT: Original=\"{originalRecipients}\", Redirected=\"{devEmail}\", Subject=\"{subject}\"");
                }
                else
                {
                    actualRecipient = originalRecipients;
                    foreach (var address in to)
                    {
                        email.To.Add(MailboxAddress.Parse(address));
                    }
                }
                // ── End redirect ──────────────────────────────────────────

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
                        Console.WriteLine("[EmailService] ❌ SMTP error: {0} (StatusCode: {1})", ex.Message, ex.StatusCode);
                        return;
                    }
                    catch (SmtpProtocolException ex)
                    {
                        Console.WriteLine("[EmailService] ❌ Protocol error: {0}", ex.Message);
                        return;
                    }
                   
                }

                if (_env.IsDevelopment())
                    Console.WriteLine($"[EmailService] ✅ Email sent successfully to \"{actualRecipient}\".");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] ❌ Email sending failed: {ex.Message}");
            }
        }
    }

}
