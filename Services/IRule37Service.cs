using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule37Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule37TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule37VerifyResult> VerifyDataAsync(Rule37VerifyRequest request);
        Task<Rule37ValidationSummary> RunValidationAsync(Rule37ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule37WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule37RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule37WorkspaceSaveResult> SaveWorkspaceAsync(Rule37ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule37WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule37ValidationRequest request);
    }
}
