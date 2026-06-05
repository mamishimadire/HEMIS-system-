using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule40Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule40TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule40ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule40VerifyResult> VerifyTablesAsync(Rule40VerifyRequest request);
        Task<Rule40ValidationSummary> RunValidationAsync(Rule40ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule40WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule40RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule40WorkspaceSaveResult> SaveWorkspaceAsync(Rule40ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule40WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule40ValidationRequest request);
    }
}
