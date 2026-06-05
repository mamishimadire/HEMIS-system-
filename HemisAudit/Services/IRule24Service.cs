using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule24Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule24TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule24AuditColumnResult> GetAuditColumnsAsync(string server, string database, string driver, string auditTable);
        Task<Rule24VerifyResult> VerifyTablesAsync(Rule24VerifyRequest request);
        Task<Rule24ValidationSummary> RunValidationAsync(Rule24ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule24WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule24RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule24WorkspaceSaveResult> SaveWorkspaceAsync(Rule24ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule24WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule24ValidationRequest request);
    }
}
