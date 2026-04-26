using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class MessagesController : Controller
    {
        private const long MaxAttachmentSizeBytes = 15 * 1024 * 1024;

        private static readonly Dictionary<string, (string ContentType, string Kind)> AllowedAttachmentTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = ("image/jpeg", "image"),
                [".jpeg"] = ("image/jpeg", "image"),
                [".png"] = ("image/png", "image"),
                [".gif"] = ("image/gif", "image"),
                [".webp"] = ("image/webp", "image"),
                [".mp3"] = ("audio/mpeg", "audio"),
                [".wav"] = ("audio/wav", "audio"),
                [".ogg"] = ("audio/ogg", "audio"),
                [".m4a"] = ("audio/mp4", "audio"),
                [".mp4"] = ("video/mp4", "video"),
                [".webm"] = ("video/webm", "video"),
                [".mov"] = ("video/quicktime", "video"),
                [".pdf"] = ("application/pdf", "file"),
                [".doc"] = ("application/msword", "file"),
                [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "file"),
                [".xls"] = ("application/vnd.ms-excel", "file"),
                [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "file"),
                [".csv"] = ("text/csv", "file"),
                [".txt"] = ("text/plain", "file"),
                [".zip"] = ("application/zip", "file")
            };

        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly IAuditLogService _audit;
        private readonly IWebHostEnvironment _environment;

        public MessagesController(
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            IAuditLogService audit,
            IWebHostEnvironment environment)
        {
            _users = users;
            _systemDb = systemDb;
            _audit = audit;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? threadId = null, int? clientId = null, bool compose = false, bool edit = false, int? editMessageId = null)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);
            var vm = await BuildPageViewModelAsync(user, role, threadId, clientId, compose, edit, editMessageId);
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Poll(int? threadId = null, int? clientId = null)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var role = await GetCurrentSystemRoleAsync(user);
            var vm = await BuildPageViewModelAsync(user, role, threadId, clientId, compose: false, edit: false, editMessageId: null);

            return Json(new
            {
                unreadCount = vm.UnreadCount,
                inbox = vm.Inbox.Select(item => new
                {
                    threadId = item.ThreadId,
                    subject = item.Subject,
                    clientName = item.ClientName,
                    preview = item.Preview,
                    lastMessageAt = item.LastMessageAt,
                    lastSenderName = item.LastSenderName,
                    unreadCount = item.UnreadCount,
                    hasUnread = item.HasUnread,
                    isActive = item.IsActive
                }),
                activeThread = vm.ActiveThread == null ? null : new
                {
                    threadId = vm.ActiveThread.ThreadId,
                    clientId = vm.ActiveThread.ClientId,
                    subject = vm.ActiveThread.Subject,
                    clientName = vm.ActiveThread.ClientName,
                    createdByName = vm.ActiveThread.CreatedByName,
                    createdAt = vm.ActiveThread.CreatedAt,
                    lastMessageAt = vm.ActiveThread.LastMessageAt,
                    canEdit = vm.ActiveThread.CanEdit,
                    canDelete = vm.ActiveThread.CanDelete,
                    participants = vm.ActiveThread.Participants,
                    messages = vm.ActiveThread.Messages.Select(message => new
                    {
                        messageId = message.MessageId,
                        threadId = message.ThreadId,
                        senderName = message.SenderName,
                        senderEmail = message.SenderEmail,
                        body = message.Body,
                        sentAt = message.SentAt,
                        isCurrentUser = message.IsCurrentUser,
                        isRead = message.IsRead,
                        readAt = message.ReadAt,
                        recipientCount = message.RecipientCount,
                        readCount = message.ReadCount,
                        firstReadAt = message.FirstReadAt,
                        lastReadAt = message.LastReadAt,
                        canEdit = message.CanEdit,
                        canDelete = message.CanDelete,
                        isEdited = message.IsEdited,
                        editedAt = message.EditedAt,
                        isDeleted = message.IsDeleted,
                        deletedAt = message.DeletedAt,
                        attachments = message.Attachments.Select(attachment => new
                        {
                            attachmentId = attachment.AttachmentId,
                            messageId = attachment.MessageId,
                            fileName = attachment.FileName,
                            filePath = attachment.FilePath,
                            contentType = attachment.ContentType,
                            fileSize = attachment.FileSize,
                            attachmentKind = attachment.AttachmentKind,
                            isImage = attachment.IsImage,
                            isAudio = attachment.IsAudio,
                            isVideo = attachment.IsVideo
                        })
                    })
                }
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(MessageComposeViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);
            var recipientIds = ResolveRecipientIds(model);
            var savedAttachments = new List<MessageAttachmentInput>();

            if (!recipientIds.Any())
                ModelState.AddModelError("", "Select at least one recipient.");
            if (string.IsNullOrWhiteSpace(model.Subject))
                ModelState.AddModelError("Subject", "Chat title is required.");

            savedAttachments = await SaveAttachmentsAsync(model.Attachments, ModelState);

            if (string.IsNullOrWhiteSpace(model.Body) && !savedAttachments.Any())
                ModelState.AddModelError("Body", "Enter a message or attach a file.");

            if (!ModelState.IsValid)
            {
                DeleteSavedAttachments(savedAttachments);
                TempData["Error"] = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { clientId = model.ClientId, compose = true });
            }

            try
            {
                var threadId = await _systemDb.CreateMessageThreadAsync(
                    user,
                    role,
                    recipientIds,
                    model.Subject.Trim(),
                    model.Body.Trim(),
                    model.ClientId,
                    savedAttachments);

                await _audit.LogAsync(
                    "TEAM_MESSAGE_SENT",
                    $"Sent message thread {threadId} to {recipientIds.Count} recipient(s).",
                    user.Id,
                    user.Email);

                TempData["Success"] = "Message sent.";
                return RedirectToAction(nameof(Index), new { threadId, clientId = model.ClientId });
            }
            catch
            {
                DeleteSavedAttachments(savedAttachments);
                throw;
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(MessageComposeViewModel model)
        {
            if (!model.ThreadId.HasValue)
                return RedirectToAction(nameof(Index));

            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);
            var savedAttachments = await SaveAttachmentsAsync(model.Attachments, ModelState);

            if (string.IsNullOrWhiteSpace(model.Body) && !savedAttachments.Any())
                ModelState.AddModelError("Body", "Enter a reply or attach a file.");

            if (!ModelState.IsValid)
            {
                DeleteSavedAttachments(savedAttachments);
                TempData["Error"] = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
            }

            try
            {
                await _systemDb.ReplyToThreadAsync(model.ThreadId.Value, user, role, model.Body.Trim(), savedAttachments);
                await _audit.LogAsync(
                    "TEAM_MESSAGE_REPLIED",
                    $"Replied in message thread {model.ThreadId.Value}.",
                    user.Id,
                    user.Email);

                TempData["Success"] = "Reply sent.";
                return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
            }
            catch
            {
                DeleteSavedAttachments(savedAttachments);
                throw;
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditThread(MessageThreadEditViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

            if (string.IsNullOrWhiteSpace(model.Subject))
            {
                TempData["Error"] = "Chat title is required.";
                return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId, edit = true });
            }

            await _systemDb.UpdateThreadSubjectAsync(model.ThreadId, user, role, model.Subject.Trim());
            await _audit.LogAsync(
                "TEAM_MESSAGE_THREAD_EDITED",
                $"Updated message thread {model.ThreadId} subject.",
                user.Id,
                user.Email);

            TempData["Success"] = "Chat updated.";
            return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMessage(MessageEditViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

            if (string.IsNullOrWhiteSpace(model.Body))
            {
                TempData["Error"] = "Message text is required.";
                return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId, editMessageId = model.MessageId });
            }

            await _systemDb.UpdateMessageAsync(model.MessageId, model.ThreadId, user, role, model.Body.Trim());
            await _audit.LogAsync(
                "TEAM_MESSAGE_EDITED",
                $"Edited message {model.MessageId} in thread {model.ThreadId}.",
                user.Id,
                user.Email);

            TempData["Success"] = "Message updated.";
            return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMessage(int messageId, int threadId, int? clientId = null)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

            await _systemDb.DeleteMessageAsync(messageId, threadId, user, role);
            await _audit.LogAsync(
                "TEAM_MESSAGE_DELETED",
                $"Deleted message {messageId} in thread {threadId}.",
                user.Id,
                user.Email);

            TempData["Success"] = "Message deleted.";
            return RedirectToAction(nameof(Index), new { threadId, clientId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteThread(int threadId, int? clientId = null)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

            await _systemDb.DeleteThreadForUserAsync(threadId, user, role);
            await _audit.LogAsync(
                "TEAM_MESSAGE_THREAD_DELETED",
                $"Deleted message thread {threadId} from the current user's inbox.",
                user.Id,
                user.Email);

            TempData["Success"] = "Chat deleted from your inbox.";
            return RedirectToAction(nameof(Index), new { clientId });
        }

        private async Task<MessagePageViewModel> BuildPageViewModelAsync(
            ApplicationUser user,
            string role,
            int? threadId,
            int? clientId,
            bool compose,
            bool edit,
            int? editMessageId)
        {
            MessageThreadViewModel? activeThread = null;
            int? composeClientId = clientId;

            if (threadId.HasValue)
            {
                var thread = await _systemDb.GetMessageThreadAsync(threadId.Value, user, role);
                if (thread != null)
                {
                    await _systemDb.MarkThreadReadAsync(threadId.Value, user, role);
                    activeThread = await _systemDb.GetMessageThreadAsync(threadId.Value, user, role);
                    composeClientId = thread.ClientId;
                }
            }

            var vm = new MessagePageViewModel
            {
                Inbox = await _systemDb.GetInboxThreadsAsync(user, role, 40),
                RecipientOptions = await _systemDb.GetMessageRecipientsAsync(user, role, composeClientId),
                UnreadCount = await _systemDb.GetUnreadMessageCountAsync(user, role),
                CurrentRole = role,
                ActiveThread = activeThread,
                SelectedThreadId = activeThread?.ThreadId,
                ShowComposeModal = compose,
                ShowEditModal = edit && activeThread != null,
                ShowEditMessageModal = editMessageId.HasValue && activeThread?.Messages.Any(m => m.MessageId == editMessageId.Value) == true,
                EditingMessageId = editMessageId,
                Compose = new MessageComposeViewModel
                {
                    ThreadId = activeThread?.ThreadId,
                    ClientId = composeClientId
                },
                EditThread = new MessageThreadEditViewModel
                {
                    ThreadId = activeThread?.ThreadId ?? 0,
                    ClientId = activeThread?.ClientId,
                    Subject = activeThread?.Subject ?? ""
                }
            };

            var editingMessage = activeThread?.Messages.FirstOrDefault(message => message.MessageId == editMessageId);
            if (editingMessage != null)
            {
                vm.EditMessage = new MessageEditViewModel
                {
                    MessageId = editingMessage.MessageId,
                    ThreadId = editingMessage.ThreadId,
                    ClientId = activeThread?.ClientId,
                    Body = editingMessage.Body
                };
            }

            foreach (var thread in vm.Inbox)
                thread.IsActive = vm.SelectedThreadId == thread.ThreadId;

            return vm;
        }

        private static List<int> ResolveRecipientIds(MessageComposeViewModel model)
        {
            var recipientIds = (model.RecipientIds ?? new List<int>()).Distinct().ToList();
            if (!recipientIds.Any() && !string.IsNullOrWhiteSpace(model.RecipientIdsCsv))
            {
                recipientIds = model.RecipientIdsCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => int.TryParse(value, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
            }

            return recipientIds;
        }

        private async Task<List<MessageAttachmentInput>> SaveAttachmentsAsync(IEnumerable<IFormFile>? files, ModelStateDictionary modelState)
        {
            var saved = new List<MessageAttachmentInput>();
            if (files == null)
                return saved;

            var attachmentsFolder = Path.Combine(_environment.WebRootPath, "uploads", "messages");
            Directory.CreateDirectory(attachmentsFolder);

            foreach (var file in files.Where(file => file != null && file.Length > 0))
            {
                var extension = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(extension) || !AllowedAttachmentTypes.TryGetValue(extension, out var fileMeta))
                {
                    modelState.AddModelError("Attachments", $"Unsupported attachment type: {file.FileName}");
                    continue;
                }

                if (file.Length > MaxAttachmentSizeBytes)
                {
                    modelState.AddModelError("Attachments", $"{file.FileName} must be 15 MB or smaller.");
                    continue;
                }

                var safeFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
                var physicalPath = Path.Combine(attachmentsFolder, safeFileName);
                await using (var stream = System.IO.File.Create(physicalPath))
                {
                    await file.CopyToAsync(stream);
                }

                saved.Add(new MessageAttachmentInput
                {
                    FileName = Path.GetFileName(file.FileName),
                    FilePath = $"/uploads/messages/{safeFileName}",
                    ContentType = fileMeta.ContentType,
                    FileSize = file.Length,
                    AttachmentKind = fileMeta.Kind
                });
            }

            return saved;
        }

        private void DeleteSavedAttachments(IEnumerable<MessageAttachmentInput> attachments)
        {
            foreach (var attachment in attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.FilePath))
                    continue;

                var relativePath = attachment.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var physicalPath = Path.Combine(_environment.WebRootPath, relativePath);
                if (System.IO.File.Exists(physicalPath))
                    System.IO.File.Delete(physicalPath);
            }
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = await _users.GetRolesAsync(user);
            return roles.FirstOrDefault() ?? "";
        }
    }
}
