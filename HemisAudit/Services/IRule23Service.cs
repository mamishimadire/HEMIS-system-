using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule23Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule23TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule23AuditColumnResult> GetAuditColumnsAsync(string server, string database, string driver, string auditTable);
        Task<Rule23VerifyResult> VerifyTablesAsync(Rule23VerifyRequest request);
        Task<Rule23ValidationSummary> RunValidationAsync(Rule23ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule23WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule23RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule23WorkspaceSaveResult> SaveWorkspaceAsync(Rule23ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule23WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule23ValidationRequest request);
    }
}
