using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule30Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule32ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule32FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string tableName, string errorTypeColumn);
        Task<Rule32VerifyResult> VerifyTableAsync(Rule32VerifyRequest request);
        Task<Rule32ValidationSummary> RunValidationAsync(Rule32ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule32WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule32RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule32WorkspaceSaveResult> SaveWorkspaceAsync(Rule32ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule32WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule32ValidationRequest request);
    }
}
