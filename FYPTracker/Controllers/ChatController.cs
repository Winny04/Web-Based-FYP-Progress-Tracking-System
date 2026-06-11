using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FYPTracker.Models;
using FYPTracker.Services.Others.Interfaces;

namespace FYPTracker.Controllers
{
    public class ChatController : Controller
    {
        private readonly FYPTrackerContext _context;
        private readonly IEmailService _emailService;

        public ChatController(FYPTrackerContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userName = HttpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(userName)) return null;
            return await _context.ApplicationUsers
                .FirstOrDefaultAsync(x => x.UserName == userName);
        }

        // GET: /Chat
        public async Task<IActionResult> Index(int? partnerId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUserId = user.Id;
            ViewBag.CurrentUserType = user.UserType;
            ViewBag.IsStudent = user.UserType == "Student";

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
                var students = await _context.FYPProjects
                    .Include(p => p.Student)
                    .Where(p => p.SupervisorId == user.Id && !p.Student.IsResign)
                    .Select(p => new ChatStudentItem {
                        Id = p.StudentId,
                        Name = p.Student.FullName,
                        Programme = p.Student.Programme
                    })
                    .ToListAsync();

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
        public async Task<IActionResult> GetHistory(int partnerId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (partnerId <= 0) return BadRequest("Invalid partner ID");

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == user.Id && m.ReceiverId == partnerId) ||
                            (m.SenderId == partnerId && m.ReceiverId == user.Id))
                .OrderBy(m => m.SentAt)
                .Select(m => new {
                    id = m.Id,
                    senderId = m.SenderId,
                    receiverId = m.ReceiverId,
                    messageText = m.MessageText,
                    sentAtStr = m.SentAt.ToString("dd MMM, HH:mm"),
                    isMine = m.SenderId == user.Id
                })
                .ToListAsync();

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
        public async Task<IActionResult> SendMessage(int receiverId, string messageText)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            if (receiverId <= 0 || string.IsNullOrWhiteSpace(messageText))
                return BadRequest("Invalid input");

            var chatMsg = new ChatMessage
            {
                SenderId = user.Id,
                ReceiverId = receiverId,
                MessageText = messageText.Trim(),
                SentAt = DateTime.Now,
                IsRead = false
            };

            _context.ChatMessages.Add(chatMsg);
            await _context.SaveChangesAsync();

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

            return Json(new {
                success = true,
                message = new {
                    id = chatMsg.Id,
                    senderId = chatMsg.SenderId,
                    receiverId = chatMsg.ReceiverId,
                    messageText = chatMsg.MessageText,
                    sentAtStr = chatMsg.SentAt.ToString("dd MMM, HH:mm"),
                    isMine = true
                }
            });
        }
    }
}
