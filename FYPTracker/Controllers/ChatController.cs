using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;
using Microsoft.AspNetCore.SignalR;
using FYPTracker.Hubs;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace FYPTracker.Controllers
{
    public class ChatController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IWebHostEnvironment _env;

        public ChatController(FYPTrackerContext context, IEmailService emailService, IHubContext<NotificationHub> hubContext, IWebHostEnvironment env)
        {
            _context = context;
            _emailService = emailService;
            _hubContext = hubContext;
            _env = env;
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userName = HttpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(userName)) return null;
            return await _context.ApplicationUsers
                .FirstOrDefaultAsync(x => x.UserName == userName || x.Email == userName);
        }

        // GET: /Chat
        public async Task<IActionResult> Index(int? partnerId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUserId = user.Id;
            ViewBag.CurrentUserType = user.UserType;
            ViewBag.IsStudent = user.UserType == "Student";
            ViewBag.MyName = user.FullName ?? user.UserName;

            if (user.UserType == "Student")
            {
                // Student: always chat with their assigned supervisor
                var project = await _context.FYPProjects
                    .Include(p => p.Supervisor)
                    .FirstOrDefaultAsync(p => p.StudentId == user.Id);

                if (project?.Supervisor != null)
                {
                    ViewBag.DefaultPartnerId = project.SupervisorId;
                    ViewBag.PartnerName = (string.IsNullOrEmpty(project.Supervisor.Title) ? "" : project.Supervisor.Title + " ") + project.Supervisor.FullName;
                }
                else
                {
                    ViewBag.NoSupervisor = true;
                }
            }
            else if (user.UserType == "Supervisor")
            {
                // Supervisor: list of active assigned students
                var projectStudents = await _context.FYPProjects
                    .Include(p => p.Student)
                    .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign)
                    .Select(p => new ChatStudentItem {
                        Id = p.StudentId,
                        Name = p.Student.FullName,
                        Programme = p.Student.Programme
                    })
                    .ToListAsync();

                var studentIds = projectStudents.Select(s => s.Id).ToList();
                var chatData = await _context.ChatMessages
                    .Where(m => (m.ReceiverId == user.Id && studentIds.Contains(m.SenderId)) ||
                                (m.SenderId == user.Id && studentIds.Contains(m.ReceiverId)))
                    .GroupBy(m => m.SenderId == user.Id ? m.ReceiverId : m.SenderId)
                    .Select(g => new {
                        StudentId = g.Key,
                        UnreadCount = g.Count(m => m.ReceiverId == user.Id && !m.IsRead),
                        LatestMessageAt = g.Max(m => m.SentAt)
                    })
                    .ToListAsync();

                foreach (var s in projectStudents)
                {
                    var data = chatData.FirstOrDefault(d => d.StudentId == s.Id);
                    if (data != null)
                    {
                        s.UnreadCount = data.UnreadCount;
                        s.LatestMessageAt = data.LatestMessageAt;
                    }
                }

                var students = projectStudents
                    .OrderByDescending(s => s.LatestMessageAt ?? DateTime.MinValue)
                    .ThenBy(s => s.Name)
                    .ToList();

                ViewBag.AssignedStudents = students;

                // Determine active chat partner
                if (partnerId.HasValue && partnerId.Value > 0)
                {
                    var selectedStudent = students.FirstOrDefault(s => s.Id == partnerId.Value);
                    if (selectedStudent != null)
                    {
                        ViewBag.DefaultPartnerId = selectedStudent.Id;
                        ViewBag.PartnerName = selectedStudent.Name;
                    }
                    else if (students.Any())
                    {
                        // Fall back to first student if partnerId invalid
                        ViewBag.DefaultPartnerId = students.First().Id;
                        ViewBag.PartnerName = students.First().Name;
                    }
                }
                else if (students.Any())
                {
                    ViewBag.DefaultPartnerId = students.First().Id;
                    ViewBag.PartnerName = students.First().Name;
                }
            }
            else
            {
                // Admin or unknown role: redirect away
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // GET: /Chat/GetHistory?partnerId=xxx
        [HttpGet]
        public async Task<IActionResult> GetHistory(int partnerId, int skip = 0, int take = 20)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (partnerId <= 0) return BadRequest("Invalid partner ID");

            var messagesQuery = await _context.ChatMessages
                .Where(m => (m.SenderId == user.Id && m.ReceiverId == partnerId) ||
                            (m.SenderId == partnerId && m.ReceiverId == user.Id))
                .OrderByDescending(m => m.SentAt)
                .Skip(skip)
                .Take(take)
                .Select(m => new {
                    id = m.Id,
                    senderId = m.SenderId,
                    receiverId = m.ReceiverId,
                    messageText = m.IsDeleted ? null : m.MessageText,
                    attachmentUrl = m.IsDeleted ? null : m.AttachmentUrl,
                    attachmentName = m.IsDeleted ? null : m.AttachmentName,
                    sentAtStr = m.SentAt.ToString("dd MMM, HH:mm"),
                    isMine = m.SenderId == user.Id,
                    isRead = m.IsRead,
                    isPinned = m.IsPinned,
                    isEdited = m.IsEdited,
                    isDeleted = m.IsDeleted,
                    replyToId = m.ReplyToId,
                    replyToMessageText = m.ReplyToMessage != null ? (m.ReplyToMessage.IsDeleted ? "Deleted Message" : (m.ReplyToMessage.MessageText ?? m.ReplyToMessage.AttachmentName)) : null
                })
                .ToListAsync();

            var messages = messagesQuery.ToList();
            messages.Reverse(); // So oldest in this batch comes first in the DOM

            // Mark unread messages as read
            var unread = await _context.ChatMessages
                .Where(m => m.ReceiverId == user.Id && m.SenderId == partnerId && !m.IsRead)
                .ToListAsync();

            if (unread.Any())
            {
                unread.ForEach(m => m.IsRead = true);
                await _context.SaveChangesAsync();
            }

            return Json(messages);
        }

        // POST: /Chat/SendMessage
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromForm] int receiverId, [FromForm] string? messageText, [FromForm] IFormFile? attachment, [FromForm] int? replyToId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (receiverId <= 0 || (string.IsNullOrWhiteSpace(messageText) && attachment == null))
                return BadRequest("Invalid input");

            string? attachmentUrl = null;
            string? attachmentName = null;

            if (attachment != null && attachment.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "chat");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + "_" + attachment.FileName;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await attachment.CopyToAsync(fileStream);
                }

                attachmentUrl = "/uploads/chat/" + uniqueFileName;
                attachmentName = attachment.FileName;
            }

            var chatMsg = new ChatMessage
            {
                SenderId = user.Id,
                ReceiverId = receiverId,
                MessageText = string.IsNullOrWhiteSpace(messageText) ? null : messageText.Trim(),
                AttachmentUrl = attachmentUrl,
                AttachmentName = attachmentName,
                SentAt = DateTime.Now,
                IsRead = false,
                ReplyToId = replyToId,
                IsPinned = false
            };

            _context.ChatMessages.Add(chatMsg);
            await _context.SaveChangesAsync();

            string? replyText = null;
            if (replyToId.HasValue)
            {
                var replyMsg = await _context.ChatMessages.FindAsync(replyToId.Value);
                replyText = replyMsg?.MessageText ?? replyMsg?.AttachmentName;
            }

            // --- Send Email Notification ---
            var receiver = await _context.ApplicationUsers.FindAsync(receiverId);
            if (receiver != null && !string.IsNullOrEmpty(receiver.Email))
            {
                    string subject = $"New message from {user.FullName} on FYP Tracker";
                    string body = $@"
                        <div style='font-family: sans-serif; color: #333;'>
                            <h3 style='color: #0d6efd;'>New Chat Message</h3>
                            <p>Hi <strong>{receiver.FullName}</strong>,</p>
                            <p>You have received a new message from <strong>{user.FullName}</strong>:</p>
                            <blockquote style='border-left: 4px solid #0d6efd; padding-left: 15px; margin-left: 0; color: #555; background: #f8f9fa; padding: 15px; border-radius: 4px;'>
                                {messageText}
                            </blockquote>
                            <p>Please log in to the FYP Progress Tracking System to reply.</p>
                            <hr style='border: 0; border-top: 1px solid #eee; margin-top: 30px;' />
                            <p style='font-size: 12px; color: #999;'>This is an automated notification. Please do not reply to this email.</p>
                        </div>";
                    
                    try {
                        await _emailService.Send(new System.Collections.Generic.List<string> { receiver.Email }, subject, body);
                    } catch {
                        // Ignore email errors to prevent chat from failing
                    }
                }
            // -------------------------------
            
            var chatMsgDto = new {
                id = chatMsg.Id,
                senderId = chatMsg.SenderId,
                receiverId = chatMsg.ReceiverId,
                messageText = chatMsg.MessageText,
                attachmentUrl = chatMsg.AttachmentUrl,
                attachmentName = chatMsg.AttachmentName,
                sentAtStr = chatMsg.SentAt.ToString("dd MMM, HH:mm"),
                isMine = false,
                isRead = false,
                isPinned = false,
                isEdited = false,
                isDeleted = false,
                replyToId = chatMsg.ReplyToId,
                replyToMessageText = replyText
            };
            
            await _hubContext.Clients.Group($"User_{receiverId}").SendAsync("ReceiveChatMessage", chatMsgDto);

            return Json(new {
                success = true,
                message = new {
                    id = chatMsg.Id,
                    senderId = chatMsg.SenderId,
                    receiverId = chatMsg.ReceiverId,
                    messageText = chatMsg.MessageText,
                    attachmentUrl = chatMsg.AttachmentUrl,
                    attachmentName = chatMsg.AttachmentName,
                    sentAtStr = chatMsg.SentAt.ToString("dd MMM, HH:mm"),
                    isMine = true,
                    isRead = false,
                    isPinned = false,
                    isEdited = false,
                    isDeleted = false,
                    replyToId = chatMsg.ReplyToId,
                    replyToMessageText = replyText
                }
            });
        }
        
        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int senderId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var unread = await _context.ChatMessages
                .Where(m => m.ReceiverId == user.Id && m.SenderId == senderId && !m.IsRead)
                .ToListAsync();

            if (unread.Any())
            {
                unread.ForEach(m => m.IsRead = true);
                await _context.SaveChangesAsync();
                
                await _hubContext.Clients.Group($"User_{senderId}").SendAsync("MessagesRead", user.Id);
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> TogglePinMessage(int messageId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null || (msg.SenderId != user.Id && msg.ReceiverId != user.Id)) return NotFound();

            msg.IsPinned = !msg.IsPinned;
            await _context.SaveChangesAsync();

            int otherUserId = msg.SenderId == user.Id ? msg.ReceiverId : msg.SenderId;
            await _hubContext.Clients.Group($"User_{otherUserId}").SendAsync("MessagePinnedToggled", msg.Id, msg.IsPinned);

            return Json(new { success = true, isPinned = msg.IsPinned });
        }

        [HttpPost]
        public async Task<IActionResult> EditMessage(int messageId, [FromForm] string newText)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(newText)) return BadRequest("Message cannot be empty.");

            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null || msg.SenderId != user.Id || msg.IsDeleted) return NotFound();

            msg.MessageText = newText.Trim();
            msg.IsEdited = true;
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group($"User_{msg.ReceiverId}").SendAsync("MessageEdited", msg.Id, msg.MessageText);

            return Json(new { success = true, newText = msg.MessageText });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null || msg.SenderId != user.Id) return NotFound();

            msg.IsDeleted = true;
            msg.MessageText = null;
            msg.AttachmentUrl = null;
            msg.AttachmentName = null;
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group($"User_{msg.ReceiverId}").SendAsync("MessageDeleted", msg.Id);

            return Json(new { success = true });
        }
    }
}
