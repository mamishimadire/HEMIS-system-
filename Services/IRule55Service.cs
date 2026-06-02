using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule55Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule55TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule55VerifyResult> VerifyDataAsync(Rule55VerifyRequest request);
        Task<Rule55ValidationSummary> RunValidationAsync(Rule55ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule55WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule55RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule55WorkspaceSaveResult> SaveWorkspaceAsync(Rule55ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule55WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule55ValidationRequest request);
    }
}
