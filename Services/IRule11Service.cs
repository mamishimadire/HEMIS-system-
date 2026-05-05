using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule11Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule11TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule11VerifyResult> VerifyTablesAsync(Rule11VerifyRequest request);
        Task<Rule11ValidationSummary> RunValidationAsync(Rule11ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule11ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule11WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule11RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule11WorkspaceSaveResult> SaveWorkspaceAsync(Rule11ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule11WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule11ValidationRequest request);
        Task<Rule11ValidationSummary> GetExportSummaryAsync(Rule11ValidationRequest request);
    }
}
