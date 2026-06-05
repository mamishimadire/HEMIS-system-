using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule52Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule52TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule52VerifyResult> VerifyTablesAsync(Rule52VerifyRequest request);
        Task<Rule52ValidationSummary> RunValidationAsync(Rule52ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule52ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule52WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule52RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule52WorkspaceSaveResult> SaveWorkspaceAsync(Rule52ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule52WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule52ValidationRequest request);
        Task<Rule52ValidationSummary> GetExportSummaryAsync(Rule52ValidationRequest request);
    }
}
