using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule12Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule12TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule12VerifyResult> VerifyTablesAsync(Rule12VerifyRequest request);
        Task<Rule12ValidationSummary> RunValidationAsync(Rule12ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule12ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule12WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule12RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule12WorkspaceSaveResult> SaveWorkspaceAsync(Rule12ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule12WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule12ValidationRequest request);
        Task<Rule12ValidationSummary> GetExportSummaryAsync(Rule12ValidationRequest request);
    }
}
