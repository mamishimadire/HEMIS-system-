using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class MessagesController : Controller
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;
        private readonly IAuditLogService _audit;

        public MessagesController(
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb,
            IAuditLogService audit)
        {
            _users = users;
            _systemDb = systemDb;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? threadId = null, int? clientId = null)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

            var vm = new MessagePageViewModel
            {
                Inbox = await _systemDb.GetInboxThreadsAsync(user, role, 40),
                RecipientOptions = await _systemDb.GetMessageRecipientsAsync(user, role, clientId),
                UnreadCount = await _systemDb.GetUnreadMessageCountAsync(user, role),
                CurrentRole = role,
                Compose = new MessageComposeViewModel
                {
                    ClientId = clientId
                }
            };

            if (threadId.HasValue)
            {
                var thread = await _systemDb.GetMessageThreadAsync(threadId.Value, user, role);
                if (thread != null)
                {
                    await _systemDb.MarkThreadReadAsync(threadId.Value, user, role);
                    vm.ActiveThread = await _systemDb.GetMessageThreadAsync(threadId.Value, user, role);
                    vm.Compose.ThreadId = threadId.Value;
                    vm.Compose.ClientId = thread.ClientId;
                }
            }

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(MessageComposeViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var role = await GetCurrentSystemRoleAsync(user);

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

            if (!recipientIds.Any())
                ModelState.AddModelError("", "Select at least one recipient.");
            if (string.IsNullOrWhiteSpace(model.Subject))
                ModelState.AddModelError("Subject", "Subject is required.");
            if (string.IsNullOrWhiteSpace(model.Body))
                ModelState.AddModelError("Body", "Message text is required.");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index), new { clientId = model.ClientId });
            }

            var threadId = await _systemDb.CreateMessageThreadAsync(user, role, recipientIds, model.Subject.Trim(), model.Body.Trim(), model.ClientId);
            await _audit.LogAsync(
                "TEAM_MESSAGE_SENT",
                $"Sent message thread {threadId} to {recipientIds.Count} recipient(s).",
                user.Id,
                user.Email);

            TempData["Success"] = "Message sent.";
            return RedirectToAction(nameof(Index), new { threadId, clientId = model.ClientId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(MessageComposeViewModel model)
        {
            if (!model.ThreadId.HasValue)
                return RedirectToAction(nameof(Index));

            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var roles = await _users.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(model.Body))
            {
                TempData["Error"] = "Reply text is required.";
                return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
            }

            await _systemDb.ReplyToThreadAsync(model.ThreadId.Value, user, role, model.Body.Trim());
            await _audit.LogAsync(
                "TEAM_MESSAGE_REPLIED",
                $"Replied in message thread {model.ThreadId.Value}.",
                user.Id,
                user.Email);

            TempData["Success"] = "Reply sent.";
            return RedirectToAction(nameof(Index), new { threadId = model.ThreadId, clientId = model.ClientId });
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
