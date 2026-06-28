using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Identity;
using HemisAudit.Models;
using HemisAudit.Services;

namespace HemisAudit.Filters
{
    public class PasswordAgeFilter : IAsyncActionFilter
    {
        private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/account/login",
            "/account/logout",
            "/account/forgotpassword",
            "/account/resetpassword",
            "/account/renewpassword",
            "/account/passwordexpired",
            "/account/changepassword",
            "/account/accessdenied"
        };

        private readonly UserManager<ApplicationUser>  _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly IPasswordPolicyService         _policy;

        public PasswordAgeFilter(UserManager<ApplicationUser> users,
                                  SignInManager<ApplicationUser> signIn,
                                  IPasswordPolicyService policy)
        {
            _users  = users;
            _signIn = signIn;
            _policy = policy;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (AllowedPaths.Contains(path)
                || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/js",  StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (context.HttpContext.User?.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            var user = await _users.GetUserAsync(context.HttpContext.User);
            if (user == null)
            {
                await next();
                return;
            }

            var now     = DateTime.UtcNow;
            var ageDays = _policy.GetPasswordAgeDays(user, now);

            if (_policy.IsPasswordExpired(user, now))
            {
                // Sign out so the session cannot be reused; user must change password before re-entering.
                await _signIn.SignOutAsync();

                if (context.Controller is Controller controller)
                    controller.TempData["PasswordExpired"] = "Your password has expired. Enter your current password and choose a new one to continue.";

                context.Result = new RedirectToActionResult("RenewPassword", "Account",
                    new { email = user.Email, expired = true });
                return;
            }

            var warningDays = _policy.GetPasswordWarningDays(user, now);
            if (warningDays.HasValue && context.Controller is Controller warningController)
            {
                warningController.TempData["PasswordWarnDays"]    = warningDays.Value;
                warningController.TempData["PasswordWarnAgeDays"] = ageDays;
            }

            await next();
        }
    }
}
