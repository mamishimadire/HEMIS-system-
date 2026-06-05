using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule51Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule51TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule51VerifyResult> VerifyTablesAsync(Rule51VerifyRequest request);
        Task<Rule51ValidationSummary> RunValidationAsync(Rule51ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule51ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule51WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule51RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule51WorkspaceSaveResult> SaveWorkspaceAsync(Rule51ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule51WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule51ValidationRequest request);
        Task<Rule51ValidationSummary> GetExportSummaryAsync(Rule51ValidationRequest request);
    }
}
