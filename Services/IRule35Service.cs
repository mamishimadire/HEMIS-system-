using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule35Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule35ColumnSelectionResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule35VerifyResult> VerifyTableAsync(Rule35VerifyRequest request);
        Task<Rule35ValidationSummary> RunValidationAsync(Rule35ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule35WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule35RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule35WorkspaceSaveResult> SaveWorkspaceAsync(Rule35ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule35WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule35ValidationRequest request);
    }
}
