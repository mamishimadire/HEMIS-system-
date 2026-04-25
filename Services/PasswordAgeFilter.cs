using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Identity;
using HemisAudit.Models;

namespace HemisAudit.Services
{
    public class PasswordAgeFilter : IAsyncActionFilter
    {
        private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/account/login",
            "/account/logout",
            "/account/forgotpassword",
            "/account/resetpassword",
            "/account/passwordexpired",
            "/account/changepassword",
            "/account/accessdenied"
        };

        private readonly UserManager<ApplicationUser> _users;
        private readonly IPasswordPolicyService _policy;

        public PasswordAgeFilter(UserManager<ApplicationUser> users, IPasswordPolicyService policy)
        {
            _users = users;
            _policy = policy;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (AllowedPaths.Contains(path) || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase))
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

            var now = DateTime.UtcNow;
            var ageDays = _policy.GetPasswordAgeDays(user, now);
            if (ageDays >= 30)
            {
                if (context.Controller is Controller controller)
                {
                    controller.TempData["PasswordExpired"] = "Your password has expired. A reset link has been sent.";
                }

                context.Result = new RedirectToActionResult("PasswordExpired", "Account", new { email = user.Email });
                return;
            }

            if (ageDays >= 25 && ageDays < 30 && context.Controller is Controller warningController)
            {
                warningController.TempData["PasswordWarnDays"] = 30 - ageDays;
            }

            await next();
        }
    }
}
