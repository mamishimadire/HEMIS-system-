using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule17Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule17ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule17FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string filterColumn);
        Task<Rule17VerifyResult> VerifyTableAsync(Rule17VerifyRequest request);
        Task<Rule17ValidationSummary> RunValidationAsync(Rule17ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule17WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule17RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule17WorkspaceSaveResult> SaveWorkspaceAsync(Rule17ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule17WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule17ValidationRequest request);
    }
}
