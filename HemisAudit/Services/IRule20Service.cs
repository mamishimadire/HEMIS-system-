using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule20Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule20TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule20ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule20VerifyResult> VerifyTablesAsync(Rule20VerifyRequest request);
        Task<Rule20ValidationSummary> RunValidationAsync(Rule20ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule20WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule20RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule20WorkspaceSaveResult> SaveWorkspaceAsync(Rule20ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule20WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule20ValidationRequest request);
        Task<Rule20ValidationSummary> GetExportSummaryAsync(Rule20ValidationRequest request);
    }
}
