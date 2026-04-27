using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule22Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule22TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule22ColumnResult> GetProfColumnsAsync(string server, string database, string driver, string profTable);
        Task<Rule22VerifyResult> VerifyTablesAsync(Rule22VerifyRequest request);
        Task<Rule22ValidationSummary> RunValidationAsync(Rule22ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule22WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule22RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule22WorkspaceSaveResult> SaveWorkspaceAsync(Rule22ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule22WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule22ValidationRequest request);
        Task<Rule22ValidationSummary> GetExportSummaryAsync(Rule22ValidationRequest request);
    }
}
