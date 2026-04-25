using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser>  _users;
        private readonly IAuditLogService              _audit;
        private readonly IPasswordPolicyService        _passwordPolicy;
        private readonly IEmailService                 _email;

        public AccountController(SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users, IAuditLogService audit,
            IPasswordPolicyService passwordPolicy, IEmailService email)
        {
            _signIn        = signIn;
            _users         = users;
            _audit         = audit;
            _passwordPolicy = passwordPolicy;
            _email         = email;
        }

        // ── Login GET ──────────────────────────────────────────────────────────
        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // ── Login POST ─────────────────────────────────────────────────────────
        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
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

                var ageDays = _passwordPolicy.GetPasswordAgeDays(user, DateTime.UtcNow);
                if (ageDays >= 30)
                {
                    await _signIn.SignOutAsync();
                    await SendPasswordResetLinkAsync(user);
                    await _audit.LogAsync("password_expired", $"Password expired at {ageDays} day(s)", user.Id, user.Email);
                    return RedirectToAction(nameof(PasswordExpired), new { email = user.Email });
                }

                if (ageDays >= 25)
                    TempData["PasswordWarnDays"] = 30 - ageDays;

                await _audit.LogAsync("login", $"User logged in", user.Id, user.Email);
                return LocalRedirect(returnUrl ?? "/Dashboard");
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
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

        // ── Change Password POST ───────────────────────────────────────────────
        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.NewPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
                return View(model);

            var result = await _users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                user.PasswordChangedAt = DateTime.UtcNow;
                user.PasswordSetDate = DateTime.UtcNow;
                var currentHash = user.PasswordHash ?? string.Empty;
                user.PasswordHistory = _passwordPolicy.BuildPasswordHistory(user.PasswordHistory, currentHash);
                await _users.UpdateAsync(user);
                await _signIn.RefreshSignInAsync(user);
                await _audit.LogAsync("change_password", null, user.Id, user.Email);
                TempData["Success"] = "Password changed successfully.";
                return RedirectToAction("Index", "Dashboard");
            }

            foreach (var e in result.Errors)
                ModelState.AddModelError("", e.Description);
            return View(model);
        }

        // â”€â”€ Forgot Password â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Password Expired â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [HttpGet, AllowAnonymous]
        public IActionResult PasswordExpired(string? email = null)
        {
            return View(new PasswordExpiredViewModel
            {
                Email = email ?? "",
                Message = "Your password has expired. A reset link has been sent to your email."
            });
        }

        // â”€â”€ Reset Password â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
            }

            await _audit.LogAsync("password_reset", "Password reset via token", user.Id, user.Email);
            TempData["Success"] = "Password updated successfully. Please sign in again.";
            return RedirectToAction(nameof(Login));
        }

        // ── Access Denied ──────────────────────────────────────────────────────
        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

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
    }
}
