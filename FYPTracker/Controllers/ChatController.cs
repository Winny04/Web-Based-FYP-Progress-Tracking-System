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
using System.Collections.Generic;

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
        public async Task<IActionResult> Index(int? partnerId, int? groupId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToAction("Login", "User");

            ViewBag.CurrentUserId = user.Id;
            ViewBag.CurrentUserType = user.UserType;
            ViewBag.IsStudent = user.UserType == "Student";
            ViewBag.MyName = user.FullName ?? user.UserName;
            ViewBag.ActiveGroupId = groupId;

            if (user.UserType == "Student")
            {
                // Student: chat with assigned supervisor or in groups
                var req = await _context.SupervisorRequests
                    .Include(r => r.Supervisor)
                    .FirstOrDefaultAsync(r => r.StudentId == user.Id && r.Status == "Approved");

                if (req?.Supervisor != null)
                {
                    ViewBag.DefaultPartnerId = req.SupervisorId;
                    ViewBag.PartnerName = (string.IsNullOrEmpty(req.Supervisor.Title) ? "" : req.Supervisor.Title + " ") + req.Supervisor.FullName;
                }
                else
                {
                    ViewBag.NoSupervisor = true;
                }

                // Load groups the student is part of
                var studentGroups = await _context.ChatGroups
                    .Include(g => g.Members)
                    .Where(g => g.Members.Any(m => m.StudentId == user.Id))
                    .ToListAsync();
                ViewBag.Groups = studentGroups;
                

                if (groupId.HasValue)
                {
                    var grp = studentGroups.FirstOrDefault(g => g.Id == groupId.Value);
                    if (grp != null) 
                    {
                        ViewBag.GroupName = grp.Name;
                        var supervisor = await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == grp.SupervisorId);
                        var members = await _context.ChatGroupMembers
                            .Include(m => m.Student)
                            .Where(m => m.GroupId == grp.Id)
                            .Select(m => new { Id = m.StudentId, Name = m.Student.FullName })
                            .ToListAsync();
                        
                        if (supervisor != null)
                        {
                            members.Add(new { Id = supervisor.Id, Name = supervisor.FullName });
                        }
                        
                        ViewBag.GroupMembers = members;
                    }
                }

            }
            else if (user.UserType == "Supervisor")
            {
                // Supervisor: list of active assigned students and groups
                var projectStudents = await _context.SupervisorRequests
                    .Include(r => r.Student)
                    .Where(r => r.SupervisorId == user.Id && r.Status == "Approved" && !r.Student.IsResign)
                    .Select(r => new ChatStudentItem {
                        Id = r.StudentId,
                        Name = r.Student.FullName,
                        Programme = r.Student.Programme
                    })
                    .ToListAsync();

                var studentIds = projectStudents.Select(s => s.Id).ToList();
                var chatData = await _context.ChatMessages
                    .Where(m => m.GroupId == null && 
                                ((m.ReceiverId == user.Id && studentIds.Contains(m.SenderId)) ||
                                 (m.SenderId == user.Id && m.ReceiverId.HasValue && studentIds.Contains(m.ReceiverId.Value))))
                    .GroupBy(m => m.SenderId == user.Id ? m.ReceiverId!.Value : m.SenderId)
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

                var supervisorGroups = await _context.ChatGroups
                    .Include(g => g.Members)
                    .Where(g => g.SupervisorId == user.Id)
                    .ToListAsync();
                ViewBag.Groups = supervisorGroups;


                if (groupId.HasValue && groupId.Value > 0)
                {
                    var selectedGrp = supervisorGroups.FirstOrDefault(g => g.Id == groupId.Value);
                    if (selectedGrp != null)
                    {
                        ViewBag.GroupName = selectedGrp.Name;
                        var members = await _context.ChatGroupMembers
                            .Include(m => m.Student)
                            .Where(m => m.GroupId == selectedGrp.Id)
                            .Select(m => new { Id = m.StudentId, Name = m.Student.FullName })
                            .ToListAsync();
                        
                        members.Add(new { Id = user.Id, Name = user.FullName });
                        
                        ViewBag.GroupMembers = members;
                    }
                }

                else if (partnerId.HasValue && partnerId.Value > 0)
                {
                    var selectedStudent = students.FirstOrDefault(s => s.Id == partnerId.Value);
                    if (selectedStudent != null)
                    {
                        ViewBag.DefaultPartnerId = selectedStudent.Id;
                        ViewBag.PartnerName = selectedStudent.Name;
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
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup(string name, List<int> memberIds)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized();

            if (string.IsNullOrWhiteSpace(name) || memberIds == null || !memberIds.Any())
            {
                return BadRequest("Invalid group data");
            }

            var grp = new ChatGroup
            {
                Name = name.Trim(),
                SupervisorId = user.Id,
                CreatedAt = DateTime.Now
            };

            foreach (var sId in memberIds)
            {
                grp.Members.Add(new ChatGroupMember { StudentId = sId });
            }

            _context.ChatGroups.Add(grp);
            await _context.SaveChangesAsync();

            return Json(new { success = true, id = grp.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupMembers(int groupId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized();

            var group = await _context.ChatGroups
                .Include(g => g.Members)
                .ThenInclude(m => m.Student)
                .FirstOrDefaultAsync(g => g.Id == groupId && g.SupervisorId == user.Id);

            if (group == null) return NotFound("Group not found or access denied.");

            var members = group.Members.Select(m => new
            {
                studentId = m.StudentId,
                name = m.Student.FullName,
                joinedAt = m.JoinedAt.ToString("dd MMM yyyy")
            }).ToList();

            return Json(members);
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableStudents(int groupId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized();

            var group = await _context.ChatGroups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == groupId && g.SupervisorId == user.Id);

            if (group == null) return NotFound("Group not found or access denied.");

            var existingMemberIds = group.Members.Select(m => m.StudentId).ToList();

            var availableStudents = await _context.SupervisorRequests
                .Include(ss => ss.Student)
                .Where(ss => ss.SupervisorId == user.Id && ss.Status == "Approved" && !ss.Student.IsResign && !existingMemberIds.Contains(ss.StudentId))
                .Select(ss => new
                {
                    id = ss.StudentId,
                    name = ss.Student.FullName
                })
                .ToListAsync();

            return Json(availableStudents);
        }

        [HttpPost]
        public async Task<IActionResult> AddGroupMember(int groupId, int studentId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized();

            var group = await _context.ChatGroups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == groupId && g.SupervisorId == user.Id);

            if (group == null) return NotFound("Group not found or access denied.");

            if (group.Members.Any(m => m.StudentId == studentId))
            {
                return BadRequest("Student is already in the group.");
            }

            var isAssigned = await _context.SupervisorRequests
                .Include(ss => ss.Student)
                .AnyAsync(ss => ss.SupervisorId == user.Id && ss.StudentId == studentId && ss.Status == "Approved" && !ss.Student.IsResign);

            if (!isAssigned) return BadRequest("Student is not assigned to you.");

            group.Members.Add(new ChatGroupMember { GroupId = groupId, StudentId = studentId, JoinedAt = DateTime.Now });
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveGroupMember(int groupId, int studentId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized();

            var group = await _context.ChatGroups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == groupId && g.SupervisorId == user.Id);

            if (group == null) return NotFound("Group not found or access denied.");

            var member = group.Members.FirstOrDefault(m => m.StudentId == studentId);
            if (member == null)
            {
                return BadRequest("Student is not in the group.");
            }

            _context.ChatGroupMembers.Remove(member);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteGroup(int groupId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType != "Supervisor") return Unauthorized(new { success = false, message = "Unauthorized" });

            var group = await _context.ChatGroups
                .FirstOrDefaultAsync(g => g.Id == groupId && g.SupervisorId == user.Id);

            if (group == null) return NotFound(new { success = false, message = "Group not found or access denied." });

            _context.ChatGroups.Remove(group);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // GET: /Chat/GetHistory
        [HttpGet]
        public async Task<IActionResult> GetHistory(int partnerId = 0, int groupId = 0, int skip = 0, int take = 20)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            
            IQueryable<ChatMessage> query;

            if (groupId > 0)
            {
                query = _context.ChatMessages
                    .Include(m => m.Sender)
                    .Include(m => m.ReplyToMessage)
                    .Where(m => m.GroupId == groupId);
            }
            else
            {
                if (partnerId <= 0) return BadRequest("Invalid partner ID");
                query = _context.ChatMessages
                    .Include(m => m.Sender)
                    .Include(m => m.ReplyToMessage)
                    .Where(m => m.GroupId == null && 
                                ((m.SenderId == user.Id && m.ReceiverId == partnerId) ||
                                 (m.SenderId == partnerId && m.ReceiverId == user.Id)));
            }

            var messagesQuery = await query
                .OrderByDescending(m => m.SentAt)
                .Skip(skip)
                .Take(take)
                .Select(m => new {
                    id = m.Id,
                    senderId = m.SenderId,
                    senderName = m.Sender.FullName,
                    receiverId = m.ReceiverId,
                    groupId = m.GroupId,
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
            messages.Reverse();

            if (groupId == 0)
            {
                // Mark unread private messages as read
                var unread = await _context.ChatMessages
                    .Where(m => m.GroupId == null && m.ReceiverId == user.Id && m.SenderId == partnerId && !m.IsRead)
                    .ToListAsync();

                if (unread.Any())
                {
                    unread.ForEach(m => m.IsRead = true);
                    await _context.SaveChangesAsync();
                }
            }

            return Json(messages);
        }

        // POST: /Chat/SendMessage
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromForm] int receiverId, [FromForm] int? groupId, [FromForm] string? messageText, [FromForm] IFormFile? attachment, [FromForm] int? replyToId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();
            
            if (!groupId.HasValue && receiverId <= 0)
                return BadRequest("Invalid input");

            if (string.IsNullOrWhiteSpace(messageText) && attachment == null)
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
                ReceiverId = groupId.HasValue ? null : receiverId,
                GroupId = groupId,
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
            
            var chatMsgDto = new {
                id = chatMsg.Id,
                senderId = chatMsg.SenderId,
                senderName = user.FullName,
                receiverId = chatMsg.ReceiverId,
                groupId = chatMsg.GroupId,
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
            
            if (groupId.HasValue)
            {
                await _hubContext.Clients.Group($"GroupChat_{groupId.Value}").SendAsync("ReceiveChatMessage", chatMsgDto);
            }
            else
            {
                await _hubContext.Clients.Group($"User_{receiverId}").SendAsync("ReceiveChatMessage", chatMsgDto);
            }

            var returnObj = new {
                id = chatMsg.Id,
                senderId = chatMsg.SenderId,
                senderName = user.FullName,
                receiverId = chatMsg.ReceiverId,
                groupId = chatMsg.GroupId,
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
            };

            return Json(new { success = true, message = returnObj });
        }
        
        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int senderId)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var unread = await _context.ChatMessages
                .Where(m => m.GroupId == null && m.ReceiverId == user.Id && m.SenderId == senderId && !m.IsRead)
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
            if (msg == null) return NotFound();

            if (msg.GroupId == null && msg.SenderId != user.Id && msg.ReceiverId != user.Id) return NotFound();

            msg.IsPinned = !msg.IsPinned;
            await _context.SaveChangesAsync();

            if (msg.GroupId.HasValue)
            {
                await _hubContext.Clients.Group($"GroupChat_{msg.GroupId.Value}").SendAsync("MessagePinnedToggled", msg.Id, msg.IsPinned);
            }
            else
            {
                int otherUserId = msg.SenderId == user.Id ? msg.ReceiverId!.Value : msg.SenderId;
                await _hubContext.Clients.Group($"User_{otherUserId}").SendAsync("MessagePinnedToggled", msg.Id, msg.IsPinned);
            }

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

            if (msg.GroupId.HasValue)
            {
                await _hubContext.Clients.Group($"GroupChat_{msg.GroupId.Value}").SendAsync("MessageEdited", msg.Id, msg.MessageText);
            }
            else
            {
                await _hubContext.Clients.Group($"User_{msg.ReceiverId!.Value}").SendAsync("MessageEdited", msg.Id, msg.MessageText);
            }

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

            if (msg.GroupId.HasValue)
            {
                await _hubContext.Clients.Group($"GroupChat_{msg.GroupId.Value}").SendAsync("MessageDeleted", msg.Id);
            }
            else
            {
                await _hubContext.Clients.Group($"User_{msg.ReceiverId!.Value}").SendAsync("MessageDeleted", msg.Id);
            }

            return Json(new { success = true });
        }
    }
}
