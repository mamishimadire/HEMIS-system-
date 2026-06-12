using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule62Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule62TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule62ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule62VerifyResult> VerifyTablesAsync(Rule62VerifyRequest request);
        Task<Rule62ValidationSummary> RunValidationAsync(Rule62ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule62ValidationSummary?> GetStoredSummaryAsync(int runId);
        Task<Rule62WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule62RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule62WorkspaceSaveResult> SaveWorkspaceAsync(Rule62ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule62WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule62ValidationRequest request);
    }
}
