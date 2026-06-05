using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule53Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule53TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule53VerifyResult> VerifyTablesAsync(Rule53VerifyRequest request);
        Task<Rule53ValidationSummary> RunValidationAsync(Rule53ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule53ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule53WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule53RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule53WorkspaceSaveResult> SaveWorkspaceAsync(Rule53ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule53WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule53ValidationRequest request);
        Task<Rule53ValidationSummary> GetExportSummaryAsync(Rule53ValidationRequest request);
    }
}
