using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace HemisAudit.Services
{
    public static class ValidationOperationHttpHelper
    {
        private const string AsyncValidationHeaderName = "X-Validation-Async";

        public static bool IsAsyncRequested(HttpRequest request) =>
            request.Headers.TryGetValue(AsyncValidationHeaderName, out StringValues values) &&
            values.Any(value => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        public static string ResolveOwnerKey(ClaimsPrincipal user) =>
            user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name
            ?? string.Empty;

        public static IActionResult Queue(
            ControllerBase controller,
            IValidationOperationService operations,
            string ownerKey,
            string label,
            Func<IServiceProvider, CancellationToken, Task<object?>> work)
        {
            var ticket = operations.Start(ownerKey, label, work);

            return controller.StatusCode(StatusCodes.Status202Accepted, new
            {
                success = true,
                pending = true,
                operationId = ticket.OperationId,
                label = ticket.Label,
                statusUrl = $"/ValidationOperations/{Uri.EscapeDataString(ticket.OperationId)}"
            });
        }
    }
}
