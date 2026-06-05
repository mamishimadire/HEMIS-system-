using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule21Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule21ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule21FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string filterColumn);
        Task<Rule21VerifyResult> VerifyTableAsync(Rule21VerifyRequest request);
        Task<Rule21ValidationSummary> RunValidationAsync(Rule21ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule21WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule21RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule21WorkspaceSaveResult> SaveWorkspaceAsync(Rule21ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule21WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule21ValidationRequest request);
    }
}
