using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule58Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule58TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule58VerifyResult> VerifyTablesAsync(Rule58VerifyRequest request);
        Task<Rule58ValidationSummary> RunValidationAsync(Rule58ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule58ValidationSummary> GetExportSummaryAsync(Rule58ValidationRequest request);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule58WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule58RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule58WorkspaceSaveResult> SaveWorkspaceAsync(Rule58ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule58WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule58ValidationRequest request);
    }
}
