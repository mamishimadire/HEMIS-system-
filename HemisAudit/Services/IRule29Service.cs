using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule29Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule29ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule29FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string filterColumn);
        Task<Rule29VerifyResult> VerifyTableAsync(Rule29VerifyRequest request);
        Task<Rule29ValidationSummary> RunValidationAsync(Rule29ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule29WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule29RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule29WorkspaceSaveResult> SaveWorkspaceAsync(Rule29ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule29WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule29ValidationRequest request);
    }
}
