using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule57Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule57TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule57VerifyResult> VerifyDataAsync(Rule57VerifyRequest request);
        Task<Rule57ValidationSummary> RunValidationAsync(Rule57ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule57WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule57RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule57WorkspaceSaveResult> SaveWorkspaceAsync(Rule57ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule57WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule57ValidationRequest request);
    }
}
