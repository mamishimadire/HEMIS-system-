using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Json;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    public class AccountController : Controller
    {
        private const string RenewPasswordVerificationSessionKey = "account.renew-password.verification";
        private static readonly TimeSpan RenewPasswordVerificationLifetime = TimeSpan.FromMinutes(10);

        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser>  _users;
        private readonly IAuditLogService              _audit;
        private readonly IPasswordPolicyService        _passwordPolicy;
        private readonly IEmailService                 _email;
        private readonly ISystemDatabaseService        _systemDb;
        private readonly IAntiforgery                  _antiforgery;

        public AccountController(SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users, IAuditLogService audit,
            IPasswordPolicyService passwordPolicy, IEmailService email,
            ISystemDatabaseService systemDb,
            IAntiforgery antiforgery)
        {
            _signIn        = signIn;
            _users         = users;
            _audit         = audit;
            _passwordPolicy = passwordPolicy;
            _email         = email;
            _systemDb      = systemDb;
            _antiforgery   = antiforgery;
        }

        // ── Login GET ──────────────────────────────────────────────────────────
        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> Login(string? returnUrl = null, bool force = false)
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            if (force)
            {
                ClearLegacyBrowserState();
            }

            if (force && User.Identity?.IsAuthenticated == true)
            {
                var user = await _users.GetUserAsync(User);
                if (user != null)
                    await _audit.LogAsync("logout", "Session reset on login screen", user.Id, user.Email);

                await _signIn.SignOutAsync();
            }

            if (!force && User.Identity?.IsAuthenticated == true && string.IsNullOrWhiteSpace(returnUrl))
                return RedirectToAction("Index", "Dashboard");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        private void ClearLegacyBrowserState()
        {
            var knownCookies = new[]
            {
                "HemisAudit.Auth",
                "HemisAudit.Auth.v1",
                "HemisAudit.Auth.v2",
                "HemisAudit.AntiForgery",
                "HemisAudit.AntiForgery.v1",
                "HemisAudit.AntiForgery.v2",
                "HemisAudit.Session.v1",
                ".AspNetCore.Session",
                ".AspNetCore.Identity.Application",
                ".AspNetCore.Mvc.CookieTempDataProvider"
            };

            foreach (var cookieName in knownCookies)
            {
                Response.Cookies.Delete(cookieName);
            }

            foreach (var requestCookie in Request.Cookies.Keys)
            {
                if (requestCookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.OrdinalIgnoreCase) ||
                    requestCookie.StartsWith(".AspNetCore.Mvc.CookieTempDataProvider", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Cookies.Delete(requestCookie);
                }
            }
        }

        // ── Login POST ─────────────────────────────────────────────────────────
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            // Validate CSRF manually so a stale token redirects to a fresh page instead of returning 400.
            try
            {
                await _antiforgery.ValidateRequestAsync(HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return RedirectToAction(nameof(Login), new { force = true });
            }

            if (!ModelState.IsValid) return View(model);

            var user = await _users.FindByEmailAsync(model.Email);
            if (user == null || !user.IsActive)
            {
                ModelState.AddModelError("", "Invalid credentials or account deactivated.");
                return View(model);
            }

            var result = await _signIn.PasswordSignInAsync(user, model.Password,
                model.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _users.UpdateAsync(user);

                var now = DateTime.UtcNow;
                var ageDays = _passwordPolicy.GetPasswordAgeDays(user, now);
                if (_passwordPolicy.IsPasswordExpired(user, now))
                {
                    await _signIn.SignOutAsync();
                    await _audit.LogAsync("password_expired", $"Password expired at {ageDays} day(s)", user.Id, user.Email);
                    TempData["PasswordExpired"] = "Your password has expired. Enter your current password and choose a new one to continue.";
                    return RedirectToAction(nameof(RenewPassword), new { email = user.Email, expired = true });
                }

                var warningDays = _passwordPolicy.GetPasswordWarningDays(user, now);
                if (warningDays.HasValue)
                {
                    TempData["PasswordWarnDays"] = warningDays.Value;
                    TempData["PasswordWarnAgeDays"] = ageDays;
                }

                await _audit.LogAsync("login", $"User logged in", user.Id, user.Email);
                return LocalRedirect(EnsureSessionStartReturnUrl(NormalizeLocalReturnUrl(returnUrl)));
            }
            if (result.IsLockedOut)
            {
                ModelState.AddModelError("", "Account locked. Try again in 15 minutes.");
                return View(model);
            }

            ModelState.AddModelError("", "Invalid email or password.");
            return View(model);
        }

        // ── Logout ─────────────────────────────────────────────────────────────
        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var user = await _users.GetUserAsync(User);
            if (user != null)
                await _audit.LogAsync("logout", null, user.Id, user.Email);
            await _signIn.SignOutAsync();
            return RedirectToAction("Login");
        }

        // ── Change Password GET ────────────────────────────────────────────────
        [HttpGet, Authorize]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            return View(new ChangePasswordViewModel
            {
                PasswordStatus = BuildPasswordStatus(user)
            });
        }

        // ── Change Password POST ───────────────────────────────────────────────
        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            if (!ModelState.IsValid)
            {
                model.PasswordStatus = BuildPasswordStatus(user);
                return View(model);
            }

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.NewPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
            {
                model.PasswordStatus = BuildPasswordStatus(user);
                return View(model);
            }

            var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                user.PasswordChangedAt = DateTime.UtcNow;
                user.PasswordSetDate = DateTime.UtcNow;
                var currentHash = user.PasswordHash ?? string.Empty;
                user.PasswordHistory = _passwordPolicy.BuildPasswordHistory(user.PasswordHistory, currentHash);
                await _users.UpdateAsync(user);
                await SyncUserMirrorAsync(user);
                await _signIn.RefreshSignInAsync(user);
                await _audit.LogAsync("change_password", null, user.Id, user.Email);
                TempData["Success"] = "Password changed successfully.";
                return RedirectToAction("Index", "Dashboard");
            }

            foreach (var e in result.Errors)
                ModelState.AddModelError("", e.Description);
            model.PasswordStatus = BuildPasswordStatus(user);
            return View(model);
        }

        [HttpGet, AllowAnonymous]
        public IActionResult RenewPassword(string? email = null, bool expired = false)
        {
            ClearRenewPasswordVerification();
            return View(new RenewPasswordViewModel
            {
                Email = email ?? string.Empty,
                IsPasswordExpiredFlow = expired,
                Step = 1
            });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> RenewPassword(RenewPasswordViewModel model)
        {
            model.Step = model.Step >= 2 ? 2 : 1;
            return model.Step == 1
                ? await VerifyRenewPasswordAsync(model)
                : await CompleteRenewPasswordAsync(model);
        }

        // â"€â"€ Forgot Password â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
        [HttpGet, AllowAnonymous]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _users.FindByEmailAsync(model.Email);
            if (user != null)
            {
                await SendPasswordResetLinkAsync(user);
                await _audit.LogAsync("password_reset_request", "Forgot password requested", user.Id, user.Email);
            }

            ViewBag.Message = "If the email exists, a reset link has been sent.";
            return View(model);
        }

        // ── Password Expired ───────────────────────────────────────────────────
        [HttpGet, AllowAnonymous]
        public IActionResult PasswordExpired(string? email = null)
        {
            return View(new PasswordExpiredViewModel
            {
                Email = email ?? "",
                Message = TempData["PasswordExpired"] as string
                    ?? "Your password has expired. Change it with your current password, or request a reset link if you forgot it."
            });
        }

        // â"€â"€ Reset Password â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
        [HttpGet, AllowAnonymous]
        public IActionResult ResetPassword(string email, string token)
        {
            return View(new ResetPasswordViewModel
            {
                Email = email,
                Token = token
            });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _users.FindByEmailAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError("", "Invalid reset request.");
                return View(model);
            }

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.NewPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
                return View(model);

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
            }
            catch
            {
                ModelState.AddModelError("", "Invalid reset token.");
                return View(model);
            }

            var result = await _users.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", e.Description);
                return View(model);
            }

            var refreshed = await _users.FindByEmailAsync(model.Email);
            if (refreshed != null)
            {
                refreshed.PasswordSetDate = DateTime.UtcNow;
                refreshed.PasswordChangedAt = DateTime.UtcNow;
                var currentHash = refreshed.PasswordHash ?? string.Empty;
                refreshed.PasswordHistory = _passwordPolicy.BuildPasswordHistory(refreshed.PasswordHistory, currentHash);
                await _users.UpdateAsync(refreshed);
                await SyncUserMirrorAsync(refreshed);
            }

            await _audit.LogAsync("password_reset", "Password reset via token", user.Id, user.Email);
            TempData["Success"] = "Password updated successfully. Please sign in again.";
            return RedirectToAction(nameof(Login));
        }

        // ── Access Denied ──────────────────────────────────────────────────────
        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        private static string EnsureSessionStartReturnUrl(string? returnUrl)
        {
            var target = string.IsNullOrWhiteSpace(returnUrl) ? "/Dashboard" : returnUrl.Trim();
            if (target.Contains("sessionStart=", StringComparison.OrdinalIgnoreCase))
                return target;

            var hashIndex = target.IndexOf('#');
            var hash = hashIndex >= 0 ? target[hashIndex..] : string.Empty;
            var pathAndQuery = hashIndex >= 0 ? target[..hashIndex] : target;
            var separator = pathAndQuery.Contains('?') ? "&" : "?";
            return $"{pathAndQuery}{separator}sessionStart=1{hash}";
        }

        private string NormalizeLocalReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
                return "/Dashboard";

            var candidate = returnUrl.Trim();
            if (Url.IsLocalUrl(candidate))
                return candidate;

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
                return "/Dashboard";

            var requestHost = Request.Host.Host ?? string.Empty;
            if (!string.Equals(absoluteUri.Host, requestHost, StringComparison.OrdinalIgnoreCase))
                return "/Dashboard";

            var requestPort = Request.Host.Port ?? (Request.IsHttps ? 443 : 80);
            var absolutePort = absoluteUri.IsDefaultPort
                ? (string.Equals(absoluteUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
                : absoluteUri.Port;
            if (absolutePort != requestPort)
                return "/Dashboard";

            var localPath = absoluteUri.PathAndQuery + absoluteUri.Fragment;
            return Url.IsLocalUrl(localPath) ? localPath : "/Dashboard";
        }

        private async Task SendPasswordResetLinkAsync(ApplicationUser user)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var url = Url.Action(nameof(ResetPassword), "Account", new { email = user.Email, token = encodedToken }, Request.Scheme) ?? string.Empty;

            var html = $@"
                <p>Hello {user.FullName},</p>
                <p>Your password needs to be reset.</p>
                <p><a href=""{url}"">Reset your password</a></p>
                <p>If the link does not open, copy this URL into your browser:</p>
                <p>{url}</p>";

            await _email.SendAsync(user.Email ?? string.Empty, "HEMIS Audit password reset", html, true);
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

        private async Task SyncUserMirrorAsync(ApplicationUser user)
        {
            var roles = await _users.GetRolesAsync(user);
            await _systemDb.EnsureUserMirrorAsync(user, roles.FirstOrDefault() ?? string.Empty);
        }

        private async Task<IActionResult> VerifyRenewPasswordAsync(RenewPasswordViewModel model)
        {
            model.Step = 1;
            model.IsVerificationConfirmed = false;
            ClearRenewPasswordVerification();

            if (!ModelState.IsValid)
                return View(model);

            var user = await _users.FindByEmailAsync(model.Email);
            if (user == null || !user.IsActive || !await _users.CheckPasswordAsync(user, model.CurrentPassword))
            {
                ModelState.AddModelError("", "Email address or old password is incorrect.");
                return View(model);
            }

            StoreRenewPasswordVerification(user);

            return View(new RenewPasswordViewModel
            {
                Email = user.Email ?? model.Email,
                IsPasswordExpiredFlow = model.IsPasswordExpiredFlow,
                Step = 2,
                IsVerificationConfirmed = true
            });
        }

        private async Task<IActionResult> CompleteRenewPasswordAsync(RenewPasswordViewModel model)
        {
            model.Step = 2;

            if (!TryGetRenewPasswordVerification(model.Email, out var verification))
                return ReturnRenewPasswordToStepOne(model, "Old password confirmation expired. Confirm it again to continue.");

            model.IsVerificationConfirmed = true;

            if (!ModelState.IsValid)
                return View(model);

            var user = await _users.FindByEmailAsync(model.Email);
            if (user == null || !user.IsActive ||
                !string.Equals(user.PasswordHash ?? string.Empty, verification.PasswordHash, StringComparison.Ordinal))
            {
                ClearRenewPasswordVerification();
                return ReturnRenewPasswordToStepOne(model, "Old password confirmation is no longer valid. Confirm it again to continue.");
            }

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.NewPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
                return View(model);

            var resetToken = await _users.GeneratePasswordResetTokenAsync(user);
            var result = await _users.ResetPasswordAsync(user, resetToken, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);
                return View(model);
            }

            var refreshed = await _users.FindByEmailAsync(model.Email);
            if (refreshed != null)
            {
                refreshed.PasswordChangedAt = DateTime.UtcNow;
                refreshed.PasswordSetDate = DateTime.UtcNow;
                var currentHash = refreshed.PasswordHash ?? string.Empty;
                refreshed.PasswordHistory = _passwordPolicy.BuildPasswordHistory(refreshed.PasswordHistory, currentHash);
                await _users.UpdateAsync(refreshed);
                await SyncUserMirrorAsync(refreshed);
            }

            ClearRenewPasswordVerification();
            await _signIn.SignOutAsync();
            await _audit.LogAsync("renew_password", "Password changed from login screen", user.Id, user.Email);

            TempData["Success"] = "Password updated successfully. Sign in with your new password.";
            return RedirectToAction(nameof(Login), new { force = true });
        }

        private IActionResult ReturnRenewPasswordToStepOne(RenewPasswordViewModel model, string message)
        {
            ModelState.Clear();
            ModelState.AddModelError("", message);

            return View(new RenewPasswordViewModel
            {
                Email = model.Email,
                IsPasswordExpiredFlow = model.IsPasswordExpiredFlow,
                Step = 1
            });
        }

        private void StoreRenewPasswordVerification(ApplicationUser user)
        {
            var state = new RenewPasswordVerificationState
            {
                Email = user.Email ?? string.Empty,
                PasswordHash = user.PasswordHash ?? string.Empty,
                VerifiedAtUtcTicks = DateTime.UtcNow.Ticks
            };

            HttpContext.Session.SetString(RenewPasswordVerificationSessionKey, JsonSerializer.Serialize(state));
        }

        private bool TryGetRenewPasswordVerification(string email, out RenewPasswordVerificationState state)
        {
            state = new RenewPasswordVerificationState();
            var payload = HttpContext.Session.GetString(RenewPasswordVerificationSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                var parsed = JsonSerializer.Deserialize<RenewPasswordVerificationState>(payload);
                if (parsed == null ||
                    string.IsNullOrWhiteSpace(parsed.Email) ||
                    string.IsNullOrWhiteSpace(parsed.PasswordHash))
                {
                    ClearRenewPasswordVerification();
                    return false;
                }

                var verifiedAtUtc = new DateTime(parsed.VerifiedAtUtcTicks, DateTimeKind.Utc);
                if (DateTime.UtcNow - verifiedAtUtc > RenewPasswordVerificationLifetime)
                {
                    ClearRenewPasswordVerification();
                    return false;
                }

                if (!string.Equals(parsed.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;

                state = parsed;
                return true;
            }
            catch
            {
                ClearRenewPasswordVerification();
                return false;
            }
        }

        private void ClearRenewPasswordVerification()
        {
            HttpContext.Session.Remove(RenewPasswordVerificationSessionKey);
        }

        private sealed class RenewPasswordVerificationState
        {
            public string Email { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
            public long VerifiedAtUtcTicks { get; set; }
        }
    }
}
