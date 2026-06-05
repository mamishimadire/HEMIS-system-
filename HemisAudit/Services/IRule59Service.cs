using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule59Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule59TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule59VerifyResult> VerifyTablesAsync(Rule59VerifyRequest request);
        Task<Rule59ValidationSummary> RunValidationAsync(Rule59ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule59ValidationSummary> GetExportSummaryAsync(Rule59ValidationRequest request);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule59WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule59RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule59WorkspaceSaveResult> SaveWorkspaceAsync(Rule59ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule59WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule59ValidationRequest request);
    }
}
