using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule19Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule19TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule19ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule19FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string studTable, string fulfilledColumn);
        Task<Rule19VerifyResult> VerifyTablesAsync(Rule19VerifyRequest request);
        Task<Rule19ValidationSummary> RunValidationAsync(Rule19ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule19WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule19RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule19WorkspaceSaveResult> SaveWorkspaceAsync(Rule19ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule19WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule19ValidationRequest request);
    }
}
