using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule11Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule11TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule11FilterValueResult> GetFilterValuesAsync(string server, string database, string driver, string qualTable, string approvalColumn);
        Task<Rule11VerifyResult> VerifyDataAsync(Rule11VerifyRequest request);
        Task<Rule11ValidationSummary> RunValidationAsync(Rule11ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule11WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule11RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule11WorkspaceSaveResult> SaveWorkspaceAsync(Rule11ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule11WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule11ValidationRequest request);
    }
}
