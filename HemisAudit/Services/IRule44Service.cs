using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule44Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule44TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule44ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule44VerifyResult> VerifyTablesAsync(Rule44VerifyRequest request);
        Task<Rule44ValidationSummary> RunValidationAsync(Rule44ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule44WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule44RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule44WorkspaceSaveResult> SaveWorkspaceAsync(Rule44ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule44WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule44ValidationRequest request);
    }
}
