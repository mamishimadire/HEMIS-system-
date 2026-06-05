using System.Collections.Concurrent;

namespace HemisAudit.Services
{
    public sealed class ValidationOperationService : IValidationOperationService
    {
        private static readonly TimeSpan CompletedRetention = TimeSpan.FromHours(12);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, ValidationOperationEntry> _operations = new(StringComparer.OrdinalIgnoreCase);

        public ValidationOperationService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public ValidationOperationTicket Start(
            string ownerKey,
            string label,
            Func<IServiceProvider, CancellationToken, Task<object?>> work)
        {
            CleanupExpiredOperations();

            var operationId = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var entry = new ValidationOperationEntry
            {
                OperationId = operationId,
                OwnerKey = NormalizeOwnerKey(ownerKey),
                Label = string.IsNullOrWhiteSpace(label) ? "Validation" : label.Trim(),
                CreatedAtUtc = now
            };

            _operations[operationId] = entry;

            _ = Task.Run(async () =>
            {
                entry.StartedAtUtc = DateTimeOffset.UtcNow;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    entry.Result = await work(scope.ServiceProvider, CancellationToken.None);
                    entry.Completed = true;
                }
                catch (Exception ex)
                {
                    entry.Completed = true;
                    entry.Failed = true;
                    entry.Error = ex.Message;
                }
                finally
                {
                    entry.Pending = false;
                    entry.CompletedAtUtc = DateTimeOffset.UtcNow;
                }
            });

            return new ValidationOperationTicket
            {
                OperationId = operationId,
                Label = entry.Label
            };
        }

        public ValidationOperationSnapshot? Get(string operationId, string ownerKey)
        {
            CleanupExpiredOperations();

            if (!_operations.TryGetValue(operationId, out var entry))
            {
                return null;
            }

            if (!string.Equals(entry.OwnerKey, NormalizeOwnerKey(ownerKey), StringComparison.Ordinal))
            {
                return null;
            }

            return new ValidationOperationSnapshot
            {
                OperationId = entry.OperationId,
                Label = entry.Label,
                Pending = entry.Pending,
                Completed = entry.Completed,
                Failed = entry.Failed,
                CreatedAtUtc = entry.CreatedAtUtc,
                StartedAtUtc = entry.StartedAtUtc,
                CompletedAtUtc = entry.CompletedAtUtc,
                Result = entry.Result,
                Error = entry.Error
            };
        }

        private void CleanupExpiredOperations()
        {
            var threshold = DateTimeOffset.UtcNow - CompletedRetention;

            foreach (var pair in _operations)
            {
                var entry = pair.Value;
                if (!entry.CompletedAtUtc.HasValue || entry.CompletedAtUtc.Value >= threshold)
                {
                    continue;
                }

                _operations.TryRemove(pair.Key, out _);
            }
        }

        private static string NormalizeOwnerKey(string ownerKey) =>
            string.IsNullOrWhiteSpace(ownerKey)
                ? string.Empty
                : ownerKey.Trim().ToUpperInvariant();

        private sealed class ValidationOperationEntry
        {
            public required string OperationId { get; init; }
            public required string OwnerKey { get; init; }
            public required string Label { get; init; }
            public required DateTimeOffset CreatedAtUtc { get; init; }
            public bool Pending { get; set; } = true;
            public bool Completed { get; set; }
            public bool Failed { get; set; }
            public DateTimeOffset? StartedAtUtc { get; set; }
            public DateTimeOffset? CompletedAtUtc { get; set; }
            public object? Result { get; set; }
            public string? Error { get; set; }
        }
    }
}
