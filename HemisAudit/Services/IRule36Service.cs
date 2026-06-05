using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule36Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, bool isStudTable);
        Task<VerifyResult> VerifyDataAsync(VerifyRequest request);
        Task<ValidationSummary> GetValidationSummaryAsync(ValidationRequest request);
        Task<ValidationSummary> RunValidationAsync(ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule36WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule36RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<WorkspaceSaveResult> SaveWorkspaceAsync(ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(ValidationRequest request);
    }
}
