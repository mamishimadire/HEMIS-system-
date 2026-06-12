using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule63Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule63TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule63ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule63VerifyResult> VerifyTablesAsync(Rule63VerifyRequest request);
        Task<Rule63ValidationSummary> RunValidationAsync(Rule63ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule63ValidationSummary?> GetStoredSummaryAsync(int runId);
        Task<Rule63WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule63RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule63WorkspaceSaveResult> SaveWorkspaceAsync(Rule63ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule63WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule63ValidationRequest request);
    }
}
