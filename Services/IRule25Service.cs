using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule25Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule25TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule25AuditColumnResult> GetAuditColumnsAsync(string server, string database, string driver, string auditTable);
        Task<Rule25VerifyResult> VerifyTablesAsync(Rule25VerifyRequest request);
        Task<Rule25ValidationSummary> RunValidationAsync(Rule25ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule25WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule25RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule25WorkspaceSaveResult> SaveWorkspaceAsync(Rule25ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule25WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule25ValidationRequest request);
    }
}
