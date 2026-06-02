using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule46Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule46TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule46ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule46VerifyResult> VerifyTablesAsync(Rule46ValidationRequest request);
        Task<Rule46ValidationSummary> RunValidationAsync(Rule46ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule46WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule46RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule46WorkspaceSaveResult> SaveWorkspaceAsync(Rule46ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule46WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateControl1Sql(Rule46ValidationRequest request);
        string GenerateControl2Sql(Rule46ValidationRequest request);
    }
}
