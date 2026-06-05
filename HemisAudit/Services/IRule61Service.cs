using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule61Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule61TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule61ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule61VerifyResult> VerifyTablesAsync(Rule61VerifyRequest request);
        Task<Rule61ValidationSummary> RunValidationAsync(Rule61ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule61ValidationSummary?> GetStoredSummaryAsync(int runId);
        Task<Rule61WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule61RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule61WorkspaceSaveResult> SaveWorkspaceAsync(Rule61ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule61WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule61ValidationRequest request);
    }
}
