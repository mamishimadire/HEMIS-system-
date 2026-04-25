using HemisAudit.Data;

namespace HemisAudit.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(string action, string? details = null, string? userId = null, string? userName = null);
    }

    public class AuditLogService : IAuditLogService
    {
        private readonly ISystemDatabaseService _systemDb;
        private readonly IHttpContextAccessor _http;

        public AuditLogService(ISystemDatabaseService systemDb, IHttpContextAccessor http)
        {
            _systemDb = systemDb;
            _http = http;
        }

        public async Task LogAsync(string action, string? details = null,
            string? userId = null, string? userName = null)
        {
            var context = _http.HttpContext;
            var ip = context?.Connection.RemoteIpAddress?.ToString();
            await _systemDb.WriteAuditLogAsync(
                action,
                details,
                userId,
                userName,
                entityType: null,
                entityId: null,
                oldValues: null,
                newValues: details,
                ipAddress: ip);
        }
    }
}
