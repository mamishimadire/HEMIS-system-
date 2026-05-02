using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HemisAudit.Services;

namespace HemisAudit.Controllers
{
    [Authorize]
    [Route("ValidationOperations")]
    public class ValidationOperationsController : Controller
    {
        private readonly IValidationOperationService _operations;

        public ValidationOperationsController(IValidationOperationService operations)
        {
            _operations = operations;
        }

        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var snapshot = _operations.Get(id, ValidationOperationHttpHelper.ResolveOwnerKey(User));
            if (snapshot == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "The validation operation was not found."
                });
            }

            if (snapshot.Pending)
            {
                return Json(new
                {
                    success = true,
                    pending = true,
                    operationId = snapshot.OperationId,
                    label = snapshot.Label,
                    createdAtUtc = snapshot.CreatedAtUtc,
                    startedAtUtc = snapshot.StartedAtUtc
                });
            }

            if (snapshot.Failed)
            {
                return Json(new
                {
                    success = false,
                    pending = false,
                    completed = true,
                    operationId = snapshot.OperationId,
                    label = snapshot.Label,
                    completedAtUtc = snapshot.CompletedAtUtc,
                    error = snapshot.Error ?? "The validation operation failed."
                });
            }

            return Json(new
            {
                success = true,
                pending = false,
                completed = true,
                operationId = snapshot.OperationId,
                label = snapshot.Label,
                completedAtUtc = snapshot.CompletedAtUtc,
                result = snapshot.Result
            });
        }
    }
}
