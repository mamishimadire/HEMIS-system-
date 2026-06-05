namespace HemisAudit.Services
{
    public interface IValidationOperationService
    {
        ValidationOperationTicket Start(
            string ownerKey,
            string label,
            Func<IServiceProvider, CancellationToken, Task<object?>> work);

        ValidationOperationSnapshot? Get(string operationId, string ownerKey);
    }

    public sealed class ValidationOperationTicket
    {
        public required string OperationId { get; init; }
        public required string Label { get; init; }
    }

    public sealed class ValidationOperationSnapshot
    {
        public required string OperationId { get; init; }
        public required string Label { get; init; }
        public required bool Pending { get; init; }
        public required bool Completed { get; init; }
        public required bool Failed { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? StartedAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public object? Result { get; init; }
        public string? Error { get; init; }
    }
}
