using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HemisAudit.Data;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private const long MaxProfileImageSizeBytes = 5 * 1024 * 1024;
        private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp"
        };

        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly ApplicationDbContext _db;
        private readonly IAuditLogService _audit;
        private readonly IPasswordPolicyService _passwordPolicy;
        private readonly ISystemDatabaseService _systemDb;
        private readonly IWebHostEnvironment _environment;

        public ProfileController(
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            ApplicationDbContext db,
            IAuditLogService audit,
            IPasswordPolicyService passwordPolicy,
            ISystemDatabaseService systemDb,
            IWebHostEnvironment environment)
        {
            _users = users;
            _signIn = signIn;
            _db = db;
            _audit = audit;
            _passwordPolicy = passwordPolicy;
            _systemDb = systemDb;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Edit()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            return View(await BuildPageViewModelAsync(user));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind(Prefix = "Edit")] ProfileEditViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var systemRole = await GetCurrentRoleAsync(user);

            if (!string.IsNullOrWhiteSpace(model.EmployeeCode))
            {
                var employeeCodeExists = await _users.Users.AnyAsync(u =>
                    u.Id != user.Id &&
                    u.EmployeeCode == model.EmployeeCode);

                if (employeeCodeExists)
                    ModelState.AddModelError("Edit.EmployeeCode", "A user with this employee code already exists.");
            }

            string? detectedExtension = null;
            if (model.ProfilePicture != null &&
                !TryValidateProfilePicture(model.ProfilePicture, out detectedExtension, out var uploadError))
            {
                ModelState.AddModelError("Edit.ProfilePicture", uploadError!);
            }

            if (!ModelState.IsValid)
                return View(await BuildPageViewModelAsync(user, model));

            var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            TrackChange(changes, "FirstName", user.FirstName, model.FirstName);
            TrackChange(changes, "LastName", user.LastName, model.LastName);
            TrackChange(changes, "EmployeeCode", user.EmployeeCode, model.EmployeeCode);
            TrackChange(changes, "PhoneNumber", user.PhoneNumber, model.PhoneNumber);
            TrackChange(changes, "Gender", user.Gender, model.Gender);
            TrackChange(changes, "Department", user.Department, model.Department);
            TrackChange(changes, "OfficeAddress", user.OfficeAddress, model.OfficeAddress);

            user.FirstName = model.FirstName.Trim();
            user.LastName = model.LastName.Trim();
            user.EmployeeCode = (model.EmployeeCode ?? string.Empty).Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
            user.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim();
            user.Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim();
            user.OfficeAddress = string.IsNullOrWhiteSpace(model.OfficeAddress) ? null : model.OfficeAddress.Trim();

            if (model.ProfilePicture != null && detectedExtension != null)
            {
                var previousPath = user.ProfilePicturePath;
                user.ProfilePicturePath = await SaveProfilePictureAsync(user.Id, model.ProfilePicture, detectedExtension, previousPath);
                if (!string.Equals(previousPath, user.ProfilePicturePath, StringComparison.OrdinalIgnoreCase))
                    changes["ProfilePicturePath"] = user.ProfilePicturePath;
            }

            await _db.SaveChangesAsync();
            await _signIn.RefreshSignInAsync(user);
            await _systemDb.EnsureUserMirrorAsync(user, systemRole);

            if (changes.Count > 0)
            {
                await _audit.LogAsync(
                    "PROFILE_UPDATED",
                    JsonSerializer.Serialize(changes),
                    user.Id,
                    user.Email);
            }

            TempData["Success"] = changes.Count > 0
                ? "Profile updated successfully."
                : "No profile changes were detected.";

            return RedirectToAction(nameof(Edit));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "PasswordChange")] ProfilePasswordChangeViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View("Edit", await BuildPageViewModelAsync(user, passwordModel: model));

            if (!await _users.CheckPasswordAsync(user, model.OldPassword))
            {
                ModelState.AddModelError("PasswordChange.OldPassword", "The current password is incorrect.");
                return View("Edit", await BuildPageViewModelAsync(user, passwordModel: model));
            }

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.NewPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("PasswordChange.NewPassword", error);

            if (!ModelState.IsValid)
                return View("Edit", await BuildPageViewModelAsync(user, passwordModel: model));

            var result = await _users.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("PasswordChange.NewPassword", error.Description);

                return View("Edit", await BuildPageViewModelAsync(user, passwordModel: model));
            }

            user.PasswordChangedAt = DateTime.UtcNow;
            user.PasswordSetDate = DateTime.UtcNow;
            user.PasswordHistory = _passwordPolicy.BuildPasswordHistory(user.PasswordHistory, user.PasswordHash ?? string.Empty);
            await _users.UpdateAsync(user);
            await _signIn.RefreshSignInAsync(user);
            await _systemDb.EnsureUserMirrorAsync(user, await GetCurrentRoleAsync(user));

            await _audit.LogAsync("PASSWORD_CHANGED", null, user.Id, user.Email);
            TempData["Success"] = "Password changed successfully.";
            return RedirectToAction(nameof(Edit));
        }

        private async Task<ProfilePageViewModel> BuildPageViewModelAsync(
            ApplicationUser user,
            ProfileEditViewModel? editModel = null,
            ProfilePasswordChangeViewModel? passwordModel = null)
        {
            var role = await GetCurrentRoleAsync(user);
            if (editModel != null)
            {
                editModel.Email = user.Email ?? string.Empty;
                editModel.SystemRole = role;
                editModel.CurrentProfilePicturePath = user.ProfilePicturePath;
            }

            return new ProfilePageViewModel
            {
                UserId = user.Id,
                Edit = editModel ?? new ProfileEditViewModel
                {
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    EmployeeCode = user.EmployeeCode,
                    Email = user.Email ?? string.Empty,
                    SystemRole = role,
                    CurrentProfilePicturePath = user.ProfilePicturePath,
                    PhoneNumber = user.PhoneNumber,
                    Gender = user.Gender,
                    Department = user.Department,
                    OfficeAddress = user.OfficeAddress
                },
                PasswordChange = passwordModel ?? new ProfilePasswordChangeViewModel(),
                PasswordStatus = BuildPasswordStatus(user)
            };
        }

        private PasswordStatusViewModel BuildPasswordStatus(ApplicationUser user)
        {
            var now = DateTime.UtcNow;
            var ageDays = _passwordPolicy.GetPasswordAgeDays(user, now);
            var daysRemaining = _passwordPolicy.GetPasswordDaysRemaining(user, now);
            var isExpired = _passwordPolicy.IsPasswordExpired(user, now);

            return new PasswordStatusViewModel
            {
                ReferenceDateUtc = _passwordPolicy.GetPasswordReferenceDate(user),
                AgeDays = ageDays,
                DaysRemaining = daysRemaining,
                MaxAgeDays = _passwordPolicy.MaxPasswordAgeDays,
                WarningWindowDays = _passwordPolicy.WarningWindowDays,
                IsExpired = isExpired,
                IsExpiringSoon = !isExpired && daysRemaining > 0 && daysRemaining <= _passwordPolicy.WarningWindowDays
            };
        }

        private async Task<string> GetCurrentRoleAsync(ApplicationUser user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = await _users.GetRolesAsync(user);
            return roles.FirstOrDefault() ?? string.Empty;
        }

        private async Task<string> SaveProfilePictureAsync(
            string userId,
            IFormFile file,
            string extension,
            string? previousVirtualPath)
        {
            var profileFolder = Path.Combine(_environment.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(profileFolder);

            var fileName = $"{userId}_{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(profileFolder, fileName);
            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream);
            }

            DeletePreviousProfilePicture(previousVirtualPath, profileFolder);
            return $"/uploads/profiles/{fileName}";
        }

        private static void DeletePreviousProfilePicture(string? previousVirtualPath, string profileFolder)
        {
            if (string.IsNullOrWhiteSpace(previousVirtualPath))
                return;

            var previousFileName = Path.GetFileName(previousVirtualPath);
            if (string.IsNullOrWhiteSpace(previousFileName))
                return;

            var previousPhysicalPath = Path.Combine(profileFolder, previousFileName);
            if (System.IO.File.Exists(previousPhysicalPath))
                System.IO.File.Delete(previousPhysicalPath);
        }

        private static bool TryValidateProfilePicture(IFormFile file, out string? extension, out string? error)
        {
            extension = null;
            error = null;

            if (file.Length <= 0)
            {
                error = "The uploaded file is empty.";
                return false;
            }

            if (file.Length > MaxProfileImageSizeBytes)
            {
                error = "Profile picture must be 5 MB or smaller.";
                return false;
            }

            if (!TryDetectImageType(file, out var detectedMimeType, out extension))
            {
                error = "Only JPG, JPEG, PNG, GIF, and WEBP images are allowed.";
                return false;
            }

            if (!AllowedMimeTypes.Contains(detectedMimeType!))
            {
                error = "Unsupported image type.";
                return false;
            }

            return true;
        }

        private static bool TryDetectImageType(IFormFile file, out string? mimeType, out string? extension)
        {
            mimeType = null;
            extension = null;

            using var stream = file.OpenReadStream();
            Span<byte> header = stackalloc byte[12];
            var bytesRead = stream.Read(header);

            if (bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF)
            {
                mimeType = "image/jpeg";
                extension = ".jpg";
                return true;
            }

            if (bytesRead >= 8 &&
                header[0] == 0x89 &&
                header[1] == 0x50 &&
                header[2] == 0x4E &&
                header[3] == 0x47 &&
                header[4] == 0x0D &&
                header[5] == 0x0A &&
                header[6] == 0x1A &&
                header[7] == 0x0A)
            {
                mimeType = "image/png";
                extension = ".png";
                return true;
            }

            if (bytesRead >= 6)
            {
                var signature = System.Text.Encoding.ASCII.GetString(header[..6]);
                if (signature == "GIF87a" || signature == "GIF89a")
                {
                    mimeType = "image/gif";
                    extension = ".gif";
                    return true;
                }
            }

            if (bytesRead >= 12)
            {
                var riff = System.Text.Encoding.ASCII.GetString(header[..4]);
                var webp = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
                if (riff == "RIFF" && webp == "WEBP")
                {
                    mimeType = "image/webp";
                    extension = ".webp";
                    return true;
                }
            }

            return false;
        }

        private static void TrackChange(IDictionary<string, string?> changes, string field, string? currentValue, string? newValue)
        {
            var current = string.IsNullOrWhiteSpace(currentValue) ? null : currentValue.Trim();
            var updated = string.IsNullOrWhiteSpace(newValue) ? null : newValue.Trim();

            if (!string.Equals(current, updated, StringComparison.Ordinal))
                changes[field] = updated;
        }
    }
}
