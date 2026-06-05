using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule38Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule38TableListResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule38VerifyResult> VerifyDataAsync(Rule38VerifyRequest request);
        Task<Rule38ValidationSummary> RunValidationAsync(Rule38ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule38WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule38RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule38WorkspaceSaveResult> SaveWorkspaceAsync(Rule38ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule38WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule38ValidationRequest request);
    }
}
