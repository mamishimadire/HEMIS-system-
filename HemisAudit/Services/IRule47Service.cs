using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule47Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule41TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule41ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule41VerifyResult> VerifyTablesAsync(Rule41VerifyRequest request);
        Task<Rule41ValidationSummary> RunValidationAsync(Rule41ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule41WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule41RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule41WorkspaceSaveResult> SaveWorkspaceAsync(Rule41ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule41WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule41ValidationRequest request);
    }
}
