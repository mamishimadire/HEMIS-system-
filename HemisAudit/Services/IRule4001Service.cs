using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule4001Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule4001TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule4001VerifyResult> VerifyTablesAsync(Rule4001VerifyRequest request);
        Task<Rule4001ValidationSummary> RunValidationAsync(Rule4001ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule4001WorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, Rule4001ValidationRequest request, string? userEmail);
        Task<Rule4001ValidationSummary?> GetFullSummaryByRunIdAsync(int runId);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule4001ValidationRequest request);
    }
}
