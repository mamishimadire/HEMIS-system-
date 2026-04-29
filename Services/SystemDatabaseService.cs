using System.Data;
using System.Text.Json;
using System.Security.Cryptography;
using BCrypt.Net;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using HemisAudit.Models;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface ISystemDatabaseService
    {
        Task<int> EnsureUserMirrorAsync(ApplicationUser user, string role);
        Task EnsurePerformanceObjectsAsync();
        Task<int> GetClientCountAsync(ApplicationUser? user, string role, string scope = "all");
        Task<int> GetPendingApprovalCountAsync(ApplicationUser? user, string role);
        Task<int> GetAssignedClientCountAsync(ApplicationUser user, string role);
        Task<string?> GetSystemRoleAsync(ApplicationUser? user);
        Task<string?> GetEngagementRoleAsync(int clientId, ApplicationUser? user, string role);
        Task ToggleClientFavoriteAsync(int clientId, ApplicationUser user, string role);
        Task<int> GetValidationRunCountAsync(ApplicationUser? user, string role);
        Task<int> GetExceptionCountAsync(ApplicationUser? user, string role);
        Task NormalizeCompletedRunStatusesAsync();
        Task<List<ClientListViewModel>> GetClientsAsync(ApplicationUser? user, string role, bool approvedOnly = false, string? search = null, string scope = "all");
        Task<List<ValidationRunRow>> GetRecentRunsAsync(ApplicationUser? user, string role, int take = 10);
        Task<List<ValidationRunRow>> GetCurrentRunsAsync(ApplicationUser? user, string role);
        Task<int> CreateClientAsync(CreateClientViewModel model, ApplicationUser creator, string role);
        Task<ClientDetailViewModel?> GetClientDetailAsync(int clientId, ApplicationUser? user, string role);
        Task ApproveClientAsync(int clientId, ApplicationUser approver, string role);
        Task<bool> CanAccessClientModuleAsync(int clientId, ApplicationUser? user, string role);
        Task<bool> CanAccessClientResultsAsync(int clientId, ApplicationUser? user, string role);
        Task<ArchiveEligibilityViewModel> GetArchiveEligibilityAsync(int clientId);
        Task ArchiveClientAsync(int clientId, ApplicationUser archiver, string role);
        Task DeleteClientAsync(int clientId);
        Task AssignUserAsync(int clientId, ApplicationUser targetUser, string engagementRole, ApplicationUser assignedBy, string assignedByRole);
        Task RemoveAssignmentAsync(int clientUserId);
        Task DeleteUserMirrorAsync(ApplicationUser targetUser, ApplicationUser deletedBy, string deletedByRole);
        Task WriteAuditLogAsync(
            string action,
            string? details = null,
            string? userId = null,
            string? userName = null,
            string? entityType = null,
            int? entityId = null,
            string? oldValues = null,
            string? newValues = null,
            string? ipAddress = null);
        Task<List<AuditLogRowViewModel>> GetAuditLogsAsync(int take = 500);
        Task<int> GetUnreadMessageCountAsync(ApplicationUser? user, string role);
        Task<List<MessageSummaryViewModel>> GetInboxThreadsAsync(ApplicationUser? user, string role, int take = 20);
        Task<MessageThreadViewModel?> GetMessageThreadAsync(int threadId, ApplicationUser? user, string role);
        Task<List<MessageRecipientOptionViewModel>> GetMessageRecipientsAsync(ApplicationUser? user, string role, int? clientId = null);
        Task<int> CreateMessageThreadAsync(ApplicationUser sender, string senderRole, IEnumerable<int> recipientUserIds, string subject, string body, int? clientId = null, IEnumerable<MessageAttachmentInput>? attachments = null);
        Task<int> ReplyToThreadAsync(int threadId, ApplicationUser sender, string senderRole, string body, IEnumerable<MessageAttachmentInput>? attachments = null);
        Task UpdateThreadSubjectAsync(int threadId, ApplicationUser user, string role, string subject);
        Task DeleteThreadForUserAsync(int threadId, ApplicationUser user, string role);
        Task UpdateMessageAsync(int messageId, int threadId, ApplicationUser user, string role, string body);
        Task DeleteMessageAsync(int messageId, int threadId, ApplicationUser user, string role);
        Task MarkThreadReadAsync(int threadId, ApplicationUser user, string role);
    }

    public class SystemDatabaseService : ISystemDatabaseService
    {
        private static readonly SemaphoreSlim NormalizeStatusesLock = new(1, 1);
        private static readonly SemaphoreSlim PerformanceObjectsLock = new(1, 1);
        private static readonly TimeSpan NormalizeStatusesInterval = TimeSpan.FromSeconds(30);
        private static DateTimeOffset _lastNormalizedStatusesAt = DateTimeOffset.MinValue;
        private static bool _performanceObjectsReady;
        private readonly IConfiguration _configuration;

        public SystemDatabaseService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<int> EnsureUserMirrorAsync(ApplicationUser user, string role)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new InvalidOperationException("User email is required.");

            await using var connection = await OpenConnectionAsync();

            var existingId = await GetUserIdByEmailAsync(connection, user.Email);
            var passwordHash = !string.IsNullOrWhiteSpace(user.PasswordHash)
                ? user.PasswordHash
                : BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), workFactor: 12);
            var passwordHistory = string.IsNullOrWhiteSpace(user.PasswordHistory)
                ? JsonSerializer.Serialize(new[] { passwordHash })
                : user.PasswordHistory;

            if (existingId.HasValue)
            {
                await using var update = connection.CreateConfiguredCommand();
                update.CommandText = @"
UPDATE dbo.Users
SET FirstName = @FirstName,
    LastName = @LastName,
    EmployeeCode = @EmployeeCode,
    PasswordHash = @PasswordHash,
    SystemRole = @SystemRole,
    IsActive = @IsActive,
    MustResetPassword = @MustResetPassword,
    PasswordSetDate = @PasswordSetDate,
    PasswordHistory = @PasswordHistory
WHERE UserID = @UserID;";
                update.Parameters.AddWithValue("@UserID", existingId.Value);
                update.Parameters.AddWithValue("@FirstName", user.FirstName ?? "");
                update.Parameters.AddWithValue("@LastName", user.LastName ?? "");
                update.Parameters.AddWithValue("@EmployeeCode", (object?)user.EmployeeCode ?? DBNull.Value);
                update.Parameters.AddWithValue("@PasswordHash", passwordHash);
                update.Parameters.AddWithValue("@SystemRole", role);
                update.Parameters.AddWithValue("@IsActive", user.IsActive);
                update.Parameters.AddWithValue("@MustResetPassword", false);
                update.Parameters.AddWithValue("@PasswordSetDate", user.PasswordSetDate ?? user.CreatedAt);
                update.Parameters.AddWithValue("@PasswordHistory", (object?)passwordHistory ?? DBNull.Value);
                await update.ExecuteNonQueryAsync();
                return existingId.Value;
            }

            await using var insert = connection.CreateConfiguredCommand();
            insert.CommandText = @"
INSERT INTO dbo.Users
(FirstName, LastName, Email, EmployeeCode, PasswordHash, SystemRole, IsActive, MustResetPassword, PasswordSetDate, PasswordHistory, CreatedAt, CreatedBy)
VALUES
(@FirstName, @LastName, @Email, @EmployeeCode, @PasswordHash, @SystemRole, @IsActive, @MustResetPassword, @PasswordSetDate, @PasswordHistory, GETDATE(), NULL);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            insert.Parameters.AddWithValue("@FirstName", user.FirstName ?? "");
            insert.Parameters.AddWithValue("@LastName", user.LastName ?? "");
            insert.Parameters.AddWithValue("@Email", user.Email);
            insert.Parameters.AddWithValue("@EmployeeCode", (object?)user.EmployeeCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("@PasswordHash", passwordHash);
            insert.Parameters.AddWithValue("@SystemRole", role);
            insert.Parameters.AddWithValue("@IsActive", user.IsActive);
            insert.Parameters.AddWithValue("@MustResetPassword", false);
            insert.Parameters.AddWithValue("@PasswordSetDate", user.PasswordSetDate ?? user.CreatedAt);
            insert.Parameters.AddWithValue("@PasswordHistory", (object?)passwordHistory ?? DBNull.Value);
            var inserted = await insert.ExecuteScalarAsync();
            return Convert.ToInt32(inserted);
        }

        public async Task EnsurePerformanceObjectsAsync()
        {
            if (_performanceObjectsReady)
                return;

            await PerformanceObjectsLock.WaitAsync();
            try
            {
                if (_performanceObjectsReady)
                    return;

                await using var connection = await OpenConnectionAsync();
                await using var command = connection.CreateConfiguredCommand();
                command.CommandTimeout = 60;
                command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ValidationRuns_ClientRuleTimestamp' AND object_id = OBJECT_ID('dbo.ValidationRuns'))
    CREATE INDEX IX_ValidationRuns_ClientRuleTimestamp
    ON dbo.ValidationRuns (ClientID, RuleNumber, RunTimestamp DESC, RunID DESC)
    INCLUDE (Status, IsCurrent, UserID, TotalRecords, PassCount, FailCount, ExceptionRate, RunByUserName, LastEditedByUserName, LastEditedAt, RecordHash);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ValidationRuns_ClientCurrentTimestamp' AND object_id = OBJECT_ID('dbo.ValidationRuns'))
    CREATE INDEX IX_ValidationRuns_ClientCurrentTimestamp
    ON dbo.ValidationRuns (ClientID, IsCurrent, RunTimestamp DESC, RunID DESC)
    INCLUDE (RuleNumber, Status, UserID, TotalRecords, PassCount, FailCount, ExceptionRate, RunByUserName, LastEditedByUserName, LastEditedAt);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReviewSignoffs_RunRole' AND object_id = OBJECT_ID('dbo.ReviewSignoffs'))
    CREATE INDEX IX_ReviewSignoffs_RunRole
    ON dbo.ReviewSignoffs (RunID, SignoffRole)
    INCLUDE (ReviewerID, Comment, SignedOffAt);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserClientAssignments_ClientUser' AND object_id = OBJECT_ID('dbo.UserClientAssignments'))
    CREATE INDEX IX_UserClientAssignments_ClientUser
    ON dbo.UserClientAssignments (ClientID, UserID)
    INCLUDE (EngagementRole);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserClientAssignments_UserClient' AND object_id = OBJECT_ID('dbo.UserClientAssignments'))
    CREATE INDEX IX_UserClientAssignments_UserClient
    ON dbo.UserClientAssignments (UserID, ClientID)
    INCLUDE (EngagementRole);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_Email' AND object_id = OBJECT_ID('dbo.Users'))
    CREATE INDEX IX_Users_Email
    ON dbo.Users (Email)
    INCLUDE (UserID, SystemRole, FirstName, LastName);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientFavorites_UserClient' AND object_id = OBJECT_ID('dbo.ClientFavorites'))
    CREATE INDEX IX_ClientFavorites_UserClient
    ON dbo.ClientFavorites (UserID, ClientID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Clients_StatusCreated' AND object_id = OBJECT_ID('dbo.Clients'))
    CREATE INDEX IX_Clients_StatusCreated
    ON dbo.Clients (Status, CreatedAt DESC, ClientID DESC)
    INCLUDE (EngagementName, MaconomyNumber, Industry, CreatedBy, DirectorName, ManagerName);";
                await command.ExecuteNonQueryAsync();
                _performanceObjectsReady = true;
            }
            finally
            {
                PerformanceObjectsLock.Release();
            }
        }

        public async Task<int> GetClientCountAsync(ApplicationUser? user, string role, string scope = "all")
        {
            var clients = await GetClientsCoreAsync(user, role, null, scope);
            return clients.Count;
        }

        public async Task<int> GetPendingApprovalCountAsync(ApplicationUser? user, string role)
        {
            var clients = await GetClientsCoreAsync(user, role);
            return clients.Count(c => string.Equals(c.Status, "Pending", StringComparison.OrdinalIgnoreCase));
        }

        public async Task<int> GetAssignedClientCountAsync(ApplicationUser user, string role)
        {
            var (userId, _) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.UserClientAssignments WHERE UserID = @UserID;";
            command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<string?> GetSystemRoleAsync(ApplicationUser? user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
                return null;

            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 SystemRole
FROM dbo.Users
WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", user.Email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        public async Task<string?> GetEngagementRoleAsync(int clientId, ApplicationUser? user, string role)
        {
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return "Admin";

            if (user == null || string.IsNullOrWhiteSpace(user.Email))
                return null;

            var userId = await ResolveUserIdByEmailAsync(user.Email);
            if (!userId.HasValue)
                return null;

            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID
  AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId.Value);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        public async Task<int> GetValidationRunCountAsync(ApplicationUser? user, string role)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            var isDataAnalyst = string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? "SELECT COUNT(*) FROM dbo.ValidationRuns;"
                : isDataAnalyst
                    ? @"SELECT COUNT(*)
                    FROM dbo.ValidationRuns vr
                    WHERE EXISTS (
                        SELECT 1
                        FROM dbo.UserClientAssignments a
                        WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                    ) OR vr.UserID = @UserID;"
                    : @"SELECT COUNT(*)
                    FROM dbo.ValidationRuns vr
                    WHERE (
                        EXISTS (
                            SELECT 1
                            FROM dbo.UserClientAssignments a
                            WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                        ) OR vr.UserID = @UserID
                    )
                      AND EXISTS (
                        SELECT 1
                        FROM dbo.ReviewSignoffs rs
                        WHERE rs.RunID = vr.RunID
                          AND rs.SignoffRole = 'DataAnalyst'
                    );";
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<int> GetExceptionCountAsync(ApplicationUser? user, string role)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            var isDataAnalyst = string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? "SELECT ISNULL(SUM(ISNULL(FailCount,0)),0) FROM dbo.ValidationRuns;"
                : isDataAnalyst
                    ? @"SELECT ISNULL(SUM(ISNULL(vr.FailCount,0)),0)
                    FROM dbo.ValidationRuns vr
                    WHERE EXISTS (
                        SELECT 1
                        FROM dbo.UserClientAssignments a
                        WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                    ) OR vr.UserID = @UserID;"
                    : @"SELECT ISNULL(SUM(ISNULL(vr.FailCount,0)),0)
                    FROM dbo.ValidationRuns vr
                    WHERE (
                        EXISTS (
                            SELECT 1
                            FROM dbo.UserClientAssignments a
                            WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                        ) OR vr.UserID = @UserID
                    )
                      AND EXISTS (
                        SELECT 1
                        FROM dbo.ReviewSignoffs rs
                        WHERE rs.RunID = vr.RunID
                          AND rs.SignoffRole = 'DataAnalyst'
                    );";
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task NormalizeCompletedRunStatusesAsync()
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastNormalizedStatusesAt < NormalizeStatusesInterval)
                return;

            await NormalizeStatusesLock.WaitAsync();
            try
            {
                now = DateTimeOffset.UtcNow;
                if (now - _lastNormalizedStatusesAt < NormalizeStatusesInterval)
                    return;

            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = 30;
            command.CommandText = @"
UPDATE vr
SET Status = 'Reviewed and Completed'
FROM dbo.ValidationRuns vr
WHERE vr.Status <> 'Reviewed and Completed'
  AND EXISTS (
      SELECT 1 FROM dbo.ReviewSignoffs rs
      WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
  )
  AND EXISTS (
      SELECT 1 FROM dbo.ReviewSignoffs rs
      WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'Manager'
  )
  AND EXISTS (
      SELECT 1 FROM dbo.ReviewSignoffs rs
      WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'Director'
  );";
            await command.ExecuteNonQueryAsync();
                _lastNormalizedStatusesAt = DateTimeOffset.UtcNow;
            }
            finally
            {
                NormalizeStatusesLock.Release();
            }
        }

        public async Task<List<ClientListViewModel>> GetClientsAsync(ApplicationUser? user, string role, bool approvedOnly = false, string? search = null, string scope = "all")
        {
            var rows = await GetClientsCoreAsync(user, role, search, scope);
            if (approvedOnly)
                rows = rows.Where(r => r.IsActiveEngagement).ToList();
            return rows;
        }

        public async Task<List<ValidationRunRow>> GetRecentRunsAsync(ApplicationUser? user, string role, int take = 10)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? $@"SELECT TOP ({take}) vr.RunID, vr.ClientID, ISNULL(c.EngagementName,'') AS ClientName, vr.RuleNumber, vr.RuleName, vr.Status, vr.TotalRecords, vr.PassCount, vr.FailCount, vr.ExceptionRate, vr.RunTimestamp,
                            COALESCE(NULLIF(vr.RunByUserName,''), LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,'')))) AS RunByUserName,
                            ISNULL(vr.LastEditedByUserName,'') AS LastEditedByUserName,
                            vr.LastEditedAt,
                            vr.IsCurrent,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'DataAnalyst'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDataAnalystSignoff
                            ,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Manager'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasManagerSignoff,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Director'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDirectorSignoff
                      FROM dbo.ValidationRuns vr
                      LEFT JOIN dbo.Clients c ON c.ClientID = vr.ClientID
                      LEFT JOIN dbo.Users u ON u.UserID = vr.UserID
                      ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;"
                : $@"SELECT TOP ({take}) vr.RunID, vr.ClientID, ISNULL(c.EngagementName,'') AS ClientName, vr.RuleNumber, vr.RuleName, vr.Status, vr.TotalRecords, vr.PassCount, vr.FailCount, vr.ExceptionRate, vr.RunTimestamp,
                            COALESCE(NULLIF(vr.RunByUserName,''), LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,'')))) AS RunByUserName,
                            ISNULL(vr.LastEditedByUserName,'') AS LastEditedByUserName,
                            vr.LastEditedAt,
                            vr.IsCurrent,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'DataAnalyst'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDataAnalystSignoff
                            ,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Manager'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasManagerSignoff,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Director'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDirectorSignoff
                      FROM dbo.ValidationRuns vr
                      LEFT JOIN dbo.Clients c ON c.ClientID = vr.ClientID
                      LEFT JOIN dbo.Users u ON u.UserID = vr.UserID
                      WHERE EXISTS (
                          SELECT 1 FROM dbo.UserClientAssignments a
                          WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                      ) OR vr.UserID = @UserID
                      ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);

            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<ValidationRunRow>();
            while (await reader.ReadAsync())
            {
                list.Add(new ValidationRunRow
                {
                    Id = reader.GetInt32(0),
                    ClientId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    ClientName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    RuleNumber = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    RuleName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Status = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    TotalValidated = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    PassCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    FailCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    ExceptionRate = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                    RunAt = reader.IsDBNull(10) ? DateTime.UtcNow : reader.GetDateTime(10),
                    RunByUserName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    LastEditedByUserName = reader.IsDBNull(12) ? null : reader.GetString(12),
                    LastEditedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                    IsCurrent = !reader.IsDBNull(14) && reader.GetBoolean(14),
                    HasDataAnalystSignoff = !reader.IsDBNull(15) && reader.GetBoolean(15),
                    HasManagerSignoff = !reader.IsDBNull(16) && reader.GetBoolean(16),
                    HasDirectorSignoff = !reader.IsDBNull(17) && reader.GetBoolean(17)
                });
            }
            return list;
        }

        public async Task<List<ValidationRunRow>> GetCurrentRunsAsync(ApplicationUser? user, string role)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? @"SELECT vr.RunID, vr.ClientID, ISNULL(c.EngagementName,'') AS ClientName, vr.RuleNumber, vr.RuleName, vr.Status, vr.TotalRecords, vr.PassCount, vr.FailCount, vr.ExceptionRate, vr.RunTimestamp,
                            COALESCE(NULLIF(vr.RunByUserName,''), LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,'')))) AS RunByUserName,
                            ISNULL(vr.LastEditedByUserName,'') AS LastEditedByUserName,
                            vr.LastEditedAt,
                            vr.IsCurrent,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'DataAnalyst'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDataAnalystSignoff
                            ,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Manager'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasManagerSignoff,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Director'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDirectorSignoff
                      FROM dbo.ValidationRuns vr
                      LEFT JOIN dbo.Clients c ON c.ClientID = vr.ClientID
                      LEFT JOIN dbo.Users u ON u.UserID = vr.UserID
                      WHERE vr.IsCurrent = 1
                      ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;"
                : @"SELECT vr.RunID, vr.ClientID, ISNULL(c.EngagementName,'') AS ClientName, vr.RuleNumber, vr.RuleName, vr.Status, vr.TotalRecords, vr.PassCount, vr.FailCount, vr.ExceptionRate, vr.RunTimestamp,
                            COALESCE(NULLIF(vr.RunByUserName,''), LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,'')))) AS RunByUserName,
                            ISNULL(vr.LastEditedByUserName,'') AS LastEditedByUserName,
                            vr.LastEditedAt,
                            vr.IsCurrent,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'DataAnalyst'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDataAnalystSignoff
                            ,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Manager'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasManagerSignoff,
                            CASE WHEN EXISTS (
                                SELECT 1 FROM dbo.ReviewSignoffs rs
                                WHERE rs.RunID = vr.RunID
                                  AND rs.SignoffRole = 'Director'
                            ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDirectorSignoff
                      FROM dbo.ValidationRuns vr
                      LEFT JOIN dbo.Clients c ON c.ClientID = vr.ClientID
                      LEFT JOIN dbo.Users u ON u.UserID = vr.UserID
                      WHERE vr.IsCurrent = 1
                        AND (
                            EXISTS (
                                SELECT 1 FROM dbo.UserClientAssignments a
                                WHERE a.ClientID = vr.ClientID AND a.UserID = @UserID
                            ) OR vr.UserID = @UserID
                        )
                      ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);

            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<ValidationRunRow>();
            while (await reader.ReadAsync())
            {
                list.Add(new ValidationRunRow
                {
                    Id = reader.GetInt32(0),
                    ClientId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    ClientName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    RuleNumber = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    RuleName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Status = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    TotalValidated = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    PassCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    FailCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    ExceptionRate = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                    RunAt = reader.IsDBNull(10) ? DateTime.UtcNow : reader.GetDateTime(10),
                    RunByUserName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    LastEditedByUserName = reader.IsDBNull(12) ? null : reader.GetString(12),
                    LastEditedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                    IsCurrent = !reader.IsDBNull(14) && reader.GetBoolean(14),
                    HasDataAnalystSignoff = !reader.IsDBNull(15) && reader.GetBoolean(15),
                    HasManagerSignoff = !reader.IsDBNull(16) && reader.GetBoolean(16),
                    HasDirectorSignoff = !reader.IsDBNull(17) && reader.GetBoolean(17)
                });
            }

            return list;
        }

        public async Task<int> CreateClientAsync(CreateClientViewModel model, ApplicationUser creator, string role)
        {
            await using var connection = await OpenConnectionAsync();
            var creatorId = await EnsureUserMirrorAsync(creator, role);
            var autoApprove = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

            await using var check = connection.CreateConfiguredCommand();
            check.CommandText = "SELECT COUNT(1) FROM dbo.Clients WHERE MaconomyNumber = @MaconomyNumber;";
            check.Parameters.AddWithValue("@MaconomyNumber", model.MaconomyNumber);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
                throw new InvalidOperationException("A client with this Maconomy number already exists.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.Clients
(EngagementName, MaconomyNumber, Industry, DirectorName, DirectorEmail, DirectorEmpCode, ManagerName, ManagerEmail, ManagerEmpCode, Status, CreatedBy, ApprovedBy, ApprovedAt, CreatedAt)
VALUES
(@EngagementName, @MaconomyNumber, @Industry, @DirectorName, @DirectorEmail, @DirectorEmpCode, @ManagerName, @ManagerEmail, @ManagerEmpCode, @Status, @CreatedBy, @ApprovedBy, @ApprovedAt, GETDATE());
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@EngagementName", model.EngagementName);
            command.Parameters.AddWithValue("@MaconomyNumber", model.MaconomyNumber);
            command.Parameters.AddWithValue("@Industry", model.Industry);
            command.Parameters.AddWithValue("@DirectorName", model.DirectorName);
            command.Parameters.AddWithValue("@DirectorEmail", model.DirectorEmail);
            command.Parameters.AddWithValue("@DirectorEmpCode", model.DirectorEmpCode);
            command.Parameters.AddWithValue("@ManagerName", model.ManagerName);
            command.Parameters.AddWithValue("@ManagerEmail", model.ManagerEmail);
            command.Parameters.AddWithValue("@ManagerEmpCode", model.ManagerEmpCode);
            command.Parameters.AddWithValue("@Status", autoApprove ? "Approved" : "Pending");
            command.Parameters.AddWithValue("@CreatedBy", creatorId);
            command.Parameters.AddWithValue("@ApprovedBy", autoApprove ? creatorId : DBNull.Value);
            command.Parameters.AddWithValue("@ApprovedAt", autoApprove ? DateTime.UtcNow : DBNull.Value);
            var created = await command.ExecuteScalarAsync();
            return Convert.ToInt32(created);
        }

        public async Task<ClientDetailViewModel?> GetClientDetailAsync(int clientId, ApplicationUser? user, string role)
        {
            if (clientId <= 0)
                return null;

            var access = await GetClientResultsAccessAsync(clientId, user, role);
            if (!access.CanAccess)
                return null;

            await using var connection = await OpenConnectionAsync();
            ClientDetailViewModel? detail = null;

            await using (var command = connection.CreateConfiguredCommand())
            {
                command.CommandText = @"
SELECT c.ClientID, c.EngagementName, c.MaconomyNumber, c.DirectorName, c.DirectorEmail, c.DirectorEmpCode,
       c.Industry, c.ManagerName, c.ManagerEmail, c.ManagerEmpCode, c.Status, c.CreatedAt,
       ISNULL(u.FirstName + ' ' + u.LastName, '') AS CreatedByName
FROM dbo.Clients c
LEFT JOIN dbo.Users u ON u.UserID = c.CreatedBy
WHERE c.ClientID = @ClientID;";
                command.Parameters.AddWithValue("@ClientID", clientId);

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return null;

                detail = new ClientDetailViewModel
                {
                    Id = reader.GetInt32(0),
                    EngagementName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    MaconomyNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DirectorName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DirectorEmail = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    DirectorEmpCode = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Industry = reader.IsDBNull(6) || string.IsNullOrWhiteSpace(reader.GetString(6)) ? "Unspecified" : reader.GetString(6),
                    ManagerName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    ManagerEmail = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    ManagerEmpCode = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    Status = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    CreatedAt = reader.IsDBNull(11) ? DateTime.UtcNow : reader.GetDateTime(11),
                    CreatedByName = reader.IsDBNull(12) ? "" : reader.GetString(12),
                    CurrentUserEngagementRole = access.CurrentUserEngagementRole
                };
            }

            detail.AssignedUsers = await GetAssignedUsersAsync(connection, clientId);
            detail.ValidationRuns = await GetValidationRunsForClientAsync(connection, clientId);
            return detail;
        }

        public async Task ApproveClientAsync(int clientId, ApplicationUser approver, string role)
        {
            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only admins can approve engagements.");

            await using var connection = await OpenConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, clientId);
            var approverId = await EnsureUserMirrorAsync(approver, role);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.Clients
SET Status = 'Approved',
    ApprovedBy = @ApprovedBy,
    ApprovedAt = GETDATE()
WHERE ClientID = @ClientID
  AND Status <> 'Approved';";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@ApprovedBy", approverId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<bool> CanAccessClientModuleAsync(int clientId, ApplicationUser? user, string role)
        {
            await using var connection = await OpenConnectionAsync();
            var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            var userId = 0;

            if (!isAdmin)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                    return false;

                userId = await EnsureUserMirrorAsync(user, role);
            }

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? @"SELECT COUNT(1)
                    FROM dbo.Clients
                    WHERE ClientID = @ClientID
                      AND Status IN ('Approved', 'Active');"
                : @"SELECT COUNT(1)
                    FROM dbo.Clients c
                    WHERE c.ClientID = @ClientID
                      AND c.Status IN ('Approved', 'Active')
                      AND EXISTS (
                          SELECT 1
                          FROM dbo.UserClientAssignments a
                          WHERE a.ClientID = c.ClientID
                            AND a.UserID = @UserID
                      );";
            command.Parameters.AddWithValue("@ClientID", clientId);
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);

            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        public async Task<bool> CanAccessClientResultsAsync(int clientId, ApplicationUser? user, string role)
        {
            if (clientId <= 0)
                return false;

            var access = await GetClientResultsAccessAsync(clientId, user, role);
            return access.CanAccess;
        }

        public async Task<ArchiveEligibilityViewModel> GetArchiveEligibilityAsync(int clientId)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT c.Status
FROM dbo.Clients c
WHERE c.ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new ArchiveEligibilityViewModel
                {
                    CanArchive = false,
                    Message = "Engagement was not found."
                };
            }

            var status = reader.IsDBNull(0) ? "" : reader.GetString(0);

            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
            {
                return new ArchiveEligibilityViewModel
                {
                    CanArchive = false,
                    Message = "This engagement is already archived."
                };
            }

            if (!string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return new ArchiveEligibilityViewModel
                {
                    CanArchive = false,
                    Message = "Only active approved engagements can be archived."
                };
            }

            await reader.DisposeAsync();
            var validationRuns = await GetValidationRunsForClientAsync(connection, clientId);
            var currentRuns = validationRuns
                .Where(run => run.IsCurrent)
                .OrderBy(run => run.RuleNumber)
                .ThenByDescending(run => run.RunAt)
                .ThenByDescending(run => run.Id)
                .ToList();

            if (currentRuns.Count == 0)
            {
                return new ArchiveEligibilityViewModel
                {
                    CanArchive = false,
                    Message = "No current results are available yet. Run the validation modules and complete the reviews before archiving."
                };
            }

            var incompleteCurrentRuns = currentRuns
                .Where(run => !string.Equals(run.Status, "Reviewed and Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var latestCurrentRun = currentRuns
                .OrderByDescending(run => run.RunAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefault();
            var canArchive = incompleteCurrentRuns.Count == 0;
            return new ArchiveEligibilityViewModel
            {
                CanArchive = canArchive,
                CurrentRunId = latestCurrentRun?.Id,
                CurrentRunRuleNumber = latestCurrentRun?.RuleNumber,
                Message = canArchive
                    ? "All current results are reviewed and completed. The engagement is ready to be archived."
                    : BuildArchiveEligibilityMessage(incompleteCurrentRuns)
            };
        }

        public async Task ArchiveClientAsync(int clientId, ApplicationUser archiver, string role)
        {
            if (!string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only a director can archive an engagement.");

            var eligibility = await GetArchiveEligibilityAsync(clientId);
            if (!eligibility.CanArchive)
                throw new InvalidOperationException(eligibility.Message);

            await using var connection = await OpenConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, clientId);
            var archiverId = await EnsureUserMirrorAsync(archiver, role);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.Clients
SET Status = 'Archived',
    ArchivedBy = @ArchivedBy,
    ArchivedAt = GETDATE()
WHERE ClientID = @ClientID
  AND Status <> 'Archived';";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@ArchivedBy", archiverId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteClientAsync(int clientId)
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, clientId);
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
DELETE FROM dbo.UserClientAssignments WHERE ClientID = @ClientID;
DELETE FROM dbo.ReviewSignoffs WHERE ClientID = @ClientID;
DELETE FROM dbo.ValidationRuns WHERE ClientID = @ClientID;
DELETE FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AssignUserAsync(int clientId, ApplicationUser targetUser, string engagementRole, ApplicationUser assignedBy, string assignedByRole)
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, clientId);
            var targetUserId = await EnsureUserMirrorAsync(targetUser, engagementRole);
            var assignedById = await EnsureUserMirrorAsync(assignedBy, assignedByRole);

            await using var exists = connection.CreateConfiguredCommand();
            exists.CommandText = "SELECT COUNT(1) FROM dbo.UserClientAssignments WHERE UserID = @UserID AND ClientID = @ClientID;";
            exists.Parameters.AddWithValue("@UserID", targetUserId);
            exists.Parameters.AddWithValue("@ClientID", clientId);
            var hasRow = Convert.ToInt32(await exists.ExecuteScalarAsync()) > 0;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = hasRow
                ? @"UPDATE dbo.UserClientAssignments
                    SET EngagementRole = @EngagementRole, AssignedBy = @AssignedBy, AssignedAt = GETDATE()
                    WHERE UserID = @UserID AND ClientID = @ClientID;"
                : @"INSERT INTO dbo.UserClientAssignments (UserID, ClientID, EngagementRole, AssignedBy, AssignedAt)
                    VALUES (@UserID, @ClientID, @EngagementRole, @AssignedBy, GETDATE());";
            command.Parameters.AddWithValue("@UserID", targetUserId);
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@EngagementRole", engagementRole);
            command.Parameters.AddWithValue("@AssignedBy", assignedById);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RemoveAssignmentAsync(int clientUserId)
        {
            await using var connection = await OpenConnectionAsync();
            await EnsureAssignmentClientNotArchivedAsync(connection, clientUserId);
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "DELETE FROM dbo.UserClientAssignments WHERE AssignmentID = @Id;";
            command.Parameters.AddWithValue("@Id", clientUserId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteUserMirrorAsync(ApplicationUser targetUser, ApplicationUser deletedBy, string deletedByRole)
        {
            if (string.IsNullOrWhiteSpace(targetUser.Email))
                return;

            var replacementUserId = await EnsureUserMirrorAsync(deletedBy, deletedByRole);

            await using var connection = await OpenConnectionAsync();
            var targetUserId = await GetUserIdByEmailAsync(connection, targetUser.Email);
            if (!targetUserId.HasValue)
                return;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.Clients
SET CreatedBy = @ReplacementUserID
WHERE CreatedBy = @TargetUserID;

UPDATE dbo.Clients
SET ApprovedBy = @ReplacementUserID
WHERE ApprovedBy = @TargetUserID;

UPDATE dbo.UserClientAssignments
SET AssignedBy = @ReplacementUserID
WHERE AssignedBy = @TargetUserID;

DELETE FROM dbo.UserClientAssignments
WHERE UserID = @TargetUserID;

DELETE FROM dbo.ReviewSignoffs
WHERE ReviewerID = @TargetUserID;

UPDATE dbo.ValidationRuns
SET UserID = @ReplacementUserID
WHERE UserID = @TargetUserID;

UPDATE dbo.AuditLog
SET UserID = NULL
WHERE UserID = @TargetUserID;

DELETE FROM dbo.PasswordResetTokens
WHERE UserID = @TargetUserID;

DELETE FROM dbo.ImpersonationLog
WHERE AdminUserID = @TargetUserID
   OR ImpersonatedUserID = @TargetUserID;

DELETE FROM dbo.Users
WHERE UserID = @TargetUserID;";
            command.Parameters.AddWithValue("@ReplacementUserID", replacementUserId);
            command.Parameters.AddWithValue("@TargetUserID", targetUserId.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task WriteAuditLogAsync(
            string action,
            string? details = null,
            string? userId = null,
            string? userName = null,
            string? entityType = null,
            int? entityId = null,
            string? oldValues = null,
            string? newValues = null,
            string? ipAddress = null)
        {
            await using var connection = await OpenConnectionAsync();
            var actorUserId = await ResolveAuditUserIdAsync(connection, userId, userName);
            var timestamp = DateTime.UtcNow;
            var previousHash = await GetLatestHashAsync(connection, "dbo.AuditLog");

            await using var insert = connection.CreateConfiguredCommand();
            insert.CommandText = @"
INSERT INTO dbo.AuditLog
(UserID, Action, EntityType, EntityID, OldValues, NewValues, IPAddress, Timestamp, PreviousHash, RecordHash)
VALUES
(@UserID, @Action, @EntityType, @EntityID, @OldValues, @NewValues, @IPAddress, @Timestamp, @PreviousHash, NULL);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            insert.Parameters.AddWithValue("@UserID", (object?)actorUserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Action", action);
            insert.Parameters.AddWithValue("@EntityType", (object?)entityType ?? DBNull.Value);
            insert.Parameters.AddWithValue("@EntityID", (object?)entityId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
            insert.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);
            insert.Parameters.AddWithValue("@IPAddress", (object?)ipAddress ?? DBNull.Value);
            insert.Parameters.AddWithValue("@Timestamp", timestamp);
            insert.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            var logId = Convert.ToInt32(await insert.ExecuteScalarAsync());

            var recordHash = ComputeHash($@"AuditLog|{logId}|{actorUserId}|{action}|{entityType}|{entityId}|{oldValues}|{newValues}|{ipAddress}|{timestamp:o}|{previousHash}");
            await using var update = connection.CreateConfiguredCommand();
            update.CommandText = "UPDATE dbo.AuditLog SET RecordHash = @RecordHash WHERE LogID = @LogID;";
            update.Parameters.AddWithValue("@RecordHash", recordHash);
            update.Parameters.AddWithValue("@LogID", logId);
            await update.ExecuteNonQueryAsync();
        }

        public async Task<List<AuditLogRowViewModel>> GetAuditLogsAsync(int take = 500)
        {
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = $@"
SELECT TOP ({take})
       al.LogID,
       al.Timestamp,
       LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS UserName,
       al.Action,
       al.EntityType,
       al.EntityID,
       al.OldValues,
       al.NewValues,
       al.IPAddress,
       al.PreviousHash,
       al.RecordHash
FROM dbo.AuditLog al
LEFT JOIN dbo.Users u ON u.UserID = al.UserID
ORDER BY al.Timestamp DESC, al.LogID DESC;";
            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<AuditLogRowViewModel>();
            while (await reader.ReadAsync())
            {
                list.Add(new AuditLogRowViewModel
                {
                    LogId = reader.GetInt32(0),
                    Timestamp = reader.IsDBNull(1) ? DateTime.UtcNow : reader.GetDateTime(1),
                    UserName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Action = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    EntityType = reader.IsDBNull(4) ? null : reader.GetString(4),
                    EntityId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    OldValues = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValues = reader.IsDBNull(7) ? null : reader.GetString(7),
                    IpAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
                    PreviousHash = reader.IsDBNull(9) ? null : reader.GetString(9),
                    RecordHash = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }

            return list;
        }

        public async Task<int> GetUnreadMessageCountAsync(ApplicationUser? user, string role)
        {
            var (userId, _) = await ResolveUserScopeAsync(user, role);
            if (userId <= 0)
                return 0;

            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"SELECT COUNT(1)
                                    FROM dbo.ThreadMessageRecipients r
                                    INNER JOIN dbo.ThreadMessages tm ON tm.MessageID = r.MessageID
                                    LEFT JOIN dbo.ThreadUserStates tus ON tus.ThreadID = tm.ThreadID AND tus.UserID = @UserID
                                    WHERE r.UserID = @UserID
                                      AND r.IsRead = 0
                                      AND ISNULL(tm.IsDeleted, 0) = 0
                                      AND ISNULL(tus.IsDeleted, 0) = 0;";
            command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<List<MessageSummaryViewModel>> GetInboxThreadsAsync(ApplicationUser? user, string role, int take = 20)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? $@"
SELECT TOP ({take})
       th.ThreadID,
       ISNULL(th.Subject,'') AS Subject,
       ISNULL(c.EngagementName,'General') AS ClientName,
       CASE
           WHEN lastMsg.MessageID IS NULL THEN ''
           WHEN ISNULL(lastMsg.IsDeleted, 0) = 1 THEN 'Message deleted'
           WHEN NULLIF(ISNULL(lastMsg.Body,''), '') IS NOT NULL THEN lastMsg.Body
           WHEN EXISTS (
               SELECT 1 FROM dbo.ThreadMessageAttachments att
               WHERE att.MessageID = lastMsg.MessageID
           ) THEN '[Attachment]'
           ELSE ''
       END AS Preview,
       ISNULL(lastMsg.SentAt, th.LastMessageAt) AS LastMessageAt,
       ISNULL(sender.FirstName + ' ' + sender.LastName, '') AS LastSenderName,
       (
         SELECT COUNT(1)
         FROM dbo.ThreadMessages tm
         INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
         WHERE tm.ThreadID = th.ThreadID
           AND r.UserID = @UserID
           AND r.IsRead = 0
           AND ISNULL(tm.IsDeleted, 0) = 0
       ) AS UnreadCount
FROM dbo.MessageThreads th
LEFT JOIN dbo.Clients c ON c.ClientID = th.ClientID
LEFT JOIN dbo.ThreadMessages lastMsg ON lastMsg.MessageID = (
    SELECT TOP 1 tm.MessageID
    FROM dbo.ThreadMessages tm
    WHERE tm.ThreadID = th.ThreadID
    ORDER BY tm.SentAt DESC, tm.MessageID DESC
)
LEFT JOIN dbo.Users sender ON sender.UserID = lastMsg.SenderUserID
WHERE (
    th.CreatedByUserID = @UserID
    OR EXISTS (
        SELECT 1
        FROM dbo.ThreadMessages tm
        INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
        WHERE tm.ThreadID = th.ThreadID
          AND r.UserID = @UserID
    )
    OR EXISTS (
        SELECT 1
        FROM dbo.ThreadMessages tm
        WHERE tm.ThreadID = th.ThreadID
          AND tm.SenderUserID = @UserID
    )
)
AND NOT EXISTS (
    SELECT 1
    FROM dbo.ThreadUserStates tus
    WHERE tus.ThreadID = th.ThreadID
      AND tus.UserID = @UserID
      AND tus.IsDeleted = 1
)
ORDER BY th.LastMessageAt DESC, th.ThreadID DESC;"
                : $@"
SELECT TOP ({take})
       th.ThreadID,
       ISNULL(th.Subject,'') AS Subject,
       ISNULL(c.EngagementName,'General') AS ClientName,
       CASE
           WHEN lastMsg.MessageID IS NULL THEN ''
           WHEN ISNULL(lastMsg.IsDeleted, 0) = 1 THEN 'Message deleted'
           WHEN NULLIF(ISNULL(lastMsg.Body,''), '') IS NOT NULL THEN lastMsg.Body
           WHEN EXISTS (
               SELECT 1 FROM dbo.ThreadMessageAttachments att
               WHERE att.MessageID = lastMsg.MessageID
           ) THEN '[Attachment]'
           ELSE ''
       END AS Preview,
       ISNULL(lastMsg.SentAt, th.LastMessageAt) AS LastMessageAt,
       ISNULL(sender.FirstName + ' ' + sender.LastName, '') AS LastSenderName,
       (
         SELECT COUNT(1)
         FROM dbo.ThreadMessages tm
         INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
         WHERE tm.ThreadID = th.ThreadID
           AND r.UserID = @UserID
           AND r.IsRead = 0
           AND ISNULL(tm.IsDeleted, 0) = 0
       ) AS UnreadCount
FROM dbo.MessageThreads th
LEFT JOIN dbo.Clients c ON c.ClientID = th.ClientID
LEFT JOIN dbo.ThreadMessages lastMsg ON lastMsg.MessageID = (
    SELECT TOP 1 tm.MessageID
    FROM dbo.ThreadMessages tm
    WHERE tm.ThreadID = th.ThreadID
    ORDER BY tm.SentAt DESC, tm.MessageID DESC
)
LEFT JOIN dbo.Users sender ON sender.UserID = lastMsg.SenderUserID
WHERE (
    EXISTS (
        SELECT 1
        FROM dbo.ThreadMessages tm
        INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
        WHERE tm.ThreadID = th.ThreadID
          AND r.UserID = @UserID
    )
    OR EXISTS (
        SELECT 1
        FROM dbo.ThreadMessages tm
        WHERE tm.ThreadID = th.ThreadID
          AND tm.SenderUserID = @UserID
    )
)
AND NOT EXISTS (
    SELECT 1
    FROM dbo.ThreadUserStates tus
    WHERE tus.ThreadID = th.ThreadID
      AND tus.UserID = @UserID
      AND tus.IsDeleted = 1
)
ORDER BY th.LastMessageAt DESC, th.ThreadID DESC;";
            command.Parameters.AddWithValue("@UserID", userId);

            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<MessageSummaryViewModel>();
            while (await reader.ReadAsync())
            {
                list.Add(new MessageSummaryViewModel
                {
                    ThreadId = reader.GetInt32(0),
                    Subject = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ClientName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Preview = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    LastMessageAt = reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4),
                    LastSenderName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    UnreadCount = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetInt32(6) : 0
                });
            }

            return list;
        }

        public async Task<MessageThreadViewModel?> GetMessageThreadAsync(int threadId, ApplicationUser? user, string role)
        {
            var (userId, _) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var check = connection.CreateConfiguredCommand();
            check.CommandText = @"SELECT COUNT(1)
                    FROM dbo.MessageThreads th
                    WHERE th.ThreadID = @ThreadID
                      AND NOT EXISTS (
                          SELECT 1
                          FROM dbo.ThreadUserStates tus
                          WHERE tus.ThreadID = th.ThreadID
                            AND tus.UserID = @UserID
                            AND tus.IsDeleted = 1
                      )
                      AND (
                          EXISTS (
                              SELECT 1 FROM dbo.ThreadMessages tm
                              INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
                              WHERE tm.ThreadID = th.ThreadID AND r.UserID = @UserID
                          )
                          OR EXISTS (
                              SELECT 1 FROM dbo.ThreadMessages tm
                              WHERE tm.ThreadID = th.ThreadID AND tm.SenderUserID = @UserID
                          )
                      );";
            check.Parameters.AddWithValue("@ThreadID", threadId);
            check.Parameters.AddWithValue("@UserID", userId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
                return null;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT th.ThreadID, th.ClientID, ISNULL(th.Subject,'') AS Subject, ISNULL(c.EngagementName,'General') AS ClientName,
       ISNULL(sender.FirstName + ' ' + sender.LastName, '') AS CreatedByName, th.CreatedAt, th.LastMessageAt
FROM dbo.MessageThreads th
LEFT JOIN dbo.Clients c ON c.ClientID = th.ClientID
LEFT JOIN dbo.Users sender ON sender.UserID = th.CreatedByUserID
WHERE th.ThreadID = @ThreadID;";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var thread = new MessageThreadViewModel
            {
                ThreadId = reader.GetInt32(0),
                ClientId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Subject = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ClientName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedByName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                CreatedAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                LastMessageAt = reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6),
                CanEdit = true,
                CanDelete = true
            };
            await reader.CloseAsync();

            await using var participants = connection.CreateConfiguredCommand();
            participants.CommandText = @"
SELECT DISTINCT participant.FullName
FROM (
    SELECT LTRIM(RTRIM(ISNULL(sender.FirstName,'') + ' ' + ISNULL(sender.LastName,''))) AS FullName
    FROM dbo.ThreadMessages tm
    INNER JOIN dbo.Users sender ON sender.UserID = tm.SenderUserID
    WHERE tm.ThreadID = @ThreadID

    UNION

    SELECT LTRIM(RTRIM(ISNULL(recipient.FirstName,'') + ' ' + ISNULL(recipient.LastName,''))) AS FullName
    FROM dbo.ThreadMessages tm
    INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
    INNER JOIN dbo.Users recipient ON recipient.UserID = r.UserID
    WHERE tm.ThreadID = @ThreadID
) participant
WHERE NULLIF(participant.FullName, '') IS NOT NULL
ORDER BY participant.FullName;";
            participants.Parameters.AddWithValue("@ThreadID", threadId);
            await using (var participantReader = await participants.ExecuteReaderAsync())
            {
                while (await participantReader.ReadAsync())
                {
                    thread.Participants.Add(participantReader.IsDBNull(0) ? "" : participantReader.GetString(0));
                }
            }

            thread.Messages = await GetThreadMessagesAsync(connection, threadId, userId);
            return thread;
        }

        public async Task<List<MessageRecipientOptionViewModel>> GetMessageRecipientsAsync(ApplicationUser? user, string role, int? clientId = null)
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = isAdmin
                ? @"SELECT u.UserID, LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS FullName, ISNULL(u.Email,'') AS Email, ISNULL(u.SystemRole,'') AS RoleName
                    FROM dbo.Users u
                    WHERE u.IsActive = 1
                      AND u.UserID <> @UserID
                    ORDER BY FullName;"
                : clientId.HasValue
                    ? @"SELECT DISTINCT u.UserID, LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS FullName, ISNULL(u.Email,'') AS Email, ISNULL(a.EngagementRole,'') AS RoleName
                        FROM dbo.Users u
                        INNER JOIN dbo.UserClientAssignments a ON a.UserID = u.UserID AND a.ClientID = @ClientID
                        WHERE u.IsActive = 1
                          AND u.UserID <> @UserID
                        ORDER BY FullName;"
                    : @"SELECT DISTINCT u.UserID, LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS FullName, ISNULL(u.Email,'') AS Email, ISNULL(a.EngagementRole,'') AS RoleName
                        FROM dbo.Users u
                        INNER JOIN dbo.UserClientAssignments a ON a.UserID = u.UserID
                        WHERE u.IsActive = 1
                          AND u.UserID <> @UserID
                          AND a.ClientID IN (
                              SELECT DISTINCT ClientID
                              FROM dbo.UserClientAssignments
                              WHERE UserID = @UserID
                          )
                        ORDER BY FullName;";
            command.Parameters.AddWithValue("@UserID", userId);
            if (clientId.HasValue)
                command.Parameters.AddWithValue("@ClientID", clientId.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var options = new List<MessageRecipientOptionViewModel>();
            while (await reader.ReadAsync())
            {
                options.Add(new MessageRecipientOptionViewModel
                {
                    UserId = reader.GetInt32(0),
                    FullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Role = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }
            return options;
        }

        public async Task<int> CreateMessageThreadAsync(ApplicationUser sender, string senderRole, IEnumerable<int> recipientUserIds, string subject, string body, int? clientId = null, IEnumerable<MessageAttachmentInput>? attachments = null)
        {
            await using var connection = await OpenConnectionAsync();
            var senderUserId = await EnsureUserMirrorAsync(sender, senderRole);
            var timestamp = DateTime.UtcNow;
            var threadPrevHash = await GetLatestHashAsync(connection, "dbo.MessageThreads");

            await using var threadCommand = connection.CreateConfiguredCommand();
            threadCommand.CommandText = @"
INSERT INTO dbo.MessageThreads (ClientID, Subject, CreatedByUserID, CreatedAt, LastMessageAt, PreviousHash, RecordHash)
VALUES (@ClientID, @Subject, @CreatedByUserID, @CreatedAt, @LastMessageAt, @PreviousHash, NULL);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            threadCommand.Parameters.AddWithValue("@ClientID", (object?)clientId ?? DBNull.Value);
            threadCommand.Parameters.AddWithValue("@Subject", subject);
            threadCommand.Parameters.AddWithValue("@CreatedByUserID", senderUserId);
            threadCommand.Parameters.AddWithValue("@CreatedAt", timestamp);
            threadCommand.Parameters.AddWithValue("@LastMessageAt", timestamp);
            threadCommand.Parameters.AddWithValue("@PreviousHash", (object?)threadPrevHash ?? DBNull.Value);
            var threadId = Convert.ToInt32(await threadCommand.ExecuteScalarAsync());

            var threadRecordHash = ComputeHash($@"MessageThread|{threadId}|{clientId}|{senderUserId}|{subject}|{timestamp:o}|{threadPrevHash}");
            await using (var updateThread = connection.CreateConfiguredCommand())
            {
                updateThread.CommandText = "UPDATE dbo.MessageThreads SET RecordHash = @RecordHash WHERE ThreadID = @ThreadID;";
                updateThread.Parameters.AddWithValue("@RecordHash", threadRecordHash);
                updateThread.Parameters.AddWithValue("@ThreadID", threadId);
                await updateThread.ExecuteNonQueryAsync();
            }

            var participantIds = recipientUserIds
                .Append(senderUserId)
                .Distinct()
                .ToList();

            await RestoreThreadForUsersAsync(connection, threadId, participantIds);
            await InsertThreadMessageAsync(connection, threadId, senderUserId, body, null, recipientUserIds, timestamp, attachments);
            return threadId;
        }

        public async Task<int> ReplyToThreadAsync(int threadId, ApplicationUser sender, string senderRole, string body, IEnumerable<MessageAttachmentInput>? attachments = null)
        {
            await using var connection = await OpenConnectionAsync();
            var senderUserId = await EnsureUserMirrorAsync(sender, senderRole);

            if (!await CanAccessThreadAsync(connection, threadId, senderUserId))
                throw new InvalidOperationException("You cannot reply to this chat.");

            var participants = await GetThreadParticipantIdsAsync(connection, threadId);
            await RestoreThreadForUsersAsync(connection, threadId, participants.Append(senderUserId));
            participants.Remove(senderUserId);
            await InsertThreadMessageAsync(connection, threadId, senderUserId, body, null, participants, DateTime.UtcNow, attachments);

            await using var update = connection.CreateConfiguredCommand();
            update.CommandText = "UPDATE dbo.MessageThreads SET LastMessageAt = GETDATE() WHERE ThreadID = @ThreadID;";
            update.Parameters.AddWithValue("@ThreadID", threadId);
            await update.ExecuteNonQueryAsync();
            return threadId;
        }

        public async Task UpdateThreadSubjectAsync(int threadId, ApplicationUser user, string role, string subject)
        {
            var userId = await EnsureUserMirrorAsync(user, role);
            await using var connection = await OpenConnectionAsync();

            if (!await CanAccessThreadAsync(connection, threadId, userId))
                throw new InvalidOperationException("You cannot edit this chat.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.MessageThreads
SET Subject = @Subject
WHERE ThreadID = @ThreadID;";
            command.Parameters.AddWithValue("@Subject", subject);
            command.Parameters.AddWithValue("@ThreadID", threadId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteThreadForUserAsync(int threadId, ApplicationUser user, string role)
        {
            var userId = await EnsureUserMirrorAsync(user, role);
            await using var connection = await OpenConnectionAsync();

            if (!await CanAccessThreadAsync(connection, threadId, userId))
                throw new InvalidOperationException("You cannot delete this chat.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
MERGE dbo.ThreadUserStates AS target
USING (SELECT @ThreadID AS ThreadID, @UserID AS UserID) AS source
    ON target.ThreadID = source.ThreadID AND target.UserID = source.UserID
WHEN MATCHED THEN
    UPDATE SET IsDeleted = 1, DeletedAt = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (ThreadID, UserID, IsDeleted, DeletedAt)
    VALUES (@ThreadID, @UserID, 1, GETDATE());";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            command.Parameters.AddWithValue("@UserID", userId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateMessageAsync(int messageId, int threadId, ApplicationUser user, string role, string body)
        {
            var userId = await EnsureUserMirrorAsync(user, role);
            await using var connection = await OpenConnectionAsync();

            if (!await CanAccessMessageAsync(connection, messageId, threadId, userId))
                throw new InvalidOperationException("You can only edit your own messages.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ThreadMessages
SET Body = @Body,
    EditedAt = GETDATE()
WHERE MessageID = @MessageID
  AND ThreadID = @ThreadID;";
            command.Parameters.AddWithValue("@Body", body);
            command.Parameters.AddWithValue("@MessageID", messageId);
            command.Parameters.AddWithValue("@ThreadID", threadId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteMessageAsync(int messageId, int threadId, ApplicationUser user, string role)
        {
            var userId = await EnsureUserMirrorAsync(user, role);
            await using var connection = await OpenConnectionAsync();

            if (!await CanAccessMessageAsync(connection, messageId, threadId, userId))
                throw new InvalidOperationException("You can only delete your own messages.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ThreadMessages
SET Body = '',
    IsDeleted = 1,
    DeletedAt = GETDATE(),
    EditedAt = GETDATE()
WHERE MessageID = @MessageID
  AND ThreadID = @ThreadID;";
            command.Parameters.AddWithValue("@MessageID", messageId);
            command.Parameters.AddWithValue("@ThreadID", threadId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task MarkThreadReadAsync(int threadId, ApplicationUser user, string role)
        {
            var userId = await EnsureUserMirrorAsync(user, role);
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE r
SET IsRead = 1,
    ReadAt = GETDATE()
FROM dbo.ThreadMessageRecipients r
INNER JOIN dbo.ThreadMessages tm ON tm.MessageID = r.MessageID
WHERE tm.ThreadID = @ThreadID
  AND r.UserID = @UserID
  AND r.IsRead = 0;";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            command.Parameters.AddWithValue("@UserID", userId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ToggleClientFavoriteAsync(int clientId, ApplicationUser user, string role)
        {
            var clients = await GetClientsCoreAsync(user, role, null, "all");
            if (!clients.Any(c => c.Id == clientId))
                throw new InvalidOperationException("You cannot favorite this engagement.");

            var (userId, _) = await ResolveUserScopeAsync(user, role);
            await using var connection = await OpenConnectionAsync();

            await using var check = connection.CreateConfiguredCommand();
            check.CommandText = @"
SELECT COUNT(1)
FROM dbo.ClientFavorites
WHERE UserID = @UserID AND ClientID = @ClientID;";
            check.Parameters.AddWithValue("@UserID", userId);
            check.Parameters.AddWithValue("@ClientID", clientId);
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;

            if (exists)
            {
                await using var delete = connection.CreateConfiguredCommand();
                delete.CommandText = @"
DELETE FROM dbo.ClientFavorites
WHERE UserID = @UserID AND ClientID = @ClientID;";
                delete.Parameters.AddWithValue("@UserID", userId);
                delete.Parameters.AddWithValue("@ClientID", clientId);
                await delete.ExecuteNonQueryAsync();
            }
            else
            {
                await using var insert = connection.CreateConfiguredCommand();
                insert.CommandText = @"
INSERT INTO dbo.ClientFavorites (UserID, ClientID)
VALUES (@UserID, @ClientID);";
                insert.Parameters.AddWithValue("@UserID", userId);
                insert.Parameters.AddWithValue("@ClientID", clientId);
                await insert.ExecuteNonQueryAsync();
            }
        }

        private async Task<List<ClientListViewModel>> GetClientsCoreAsync(ApplicationUser? user, string role, string? search = null, string scope = "all")
        {
            var (userId, isAdmin) = await ResolveUserScopeAsync(user, role);
            var isDirector = string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            var isDataAnalyst = string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase);
            var normalizedSearch = string.IsNullOrWhiteSpace(search)
                ? null
                : $"%{search.Trim().ToLowerInvariant()}%";
            var normalizedScope = string.IsNullOrWhiteSpace(scope)
                ? "all"
                : scope.Trim().ToLowerInvariant();
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = 60;
            command.CommandText = isAdmin
                ? @"
SELECT c.ClientID, c.EngagementName, c.MaconomyNumber, ISNULL(c.Industry, '') AS Industry, c.Status, c.CreatedAt,
       ISNULL(u.FirstName + ' ' + u.LastName, '') AS CreatedByName,
       (SELECT COUNT(1) FROM dbo.UserClientAssignments a WHERE a.ClientID = c.ClientID) AS AssignedUsersCount,
       (SELECT COUNT(1) FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID) AS ValidationRunsCount,
       (SELECT COUNT(1)
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )) AS SignedOffValidationRunsCount,
       (SELECT TOP 1 vr.RunID FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunId,
       (SELECT TOP 1 vr.RuleNumber FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunRuleNumber,
       (SELECT TOP 1 vr.RunID
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunId,
       (SELECT TOP 1 vr.RuleNumber
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunRuleNumber,
       (SELECT TOP 1 vr.Status FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunStatus,
       (SELECT TOP 1 vr.RunTimestamp FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunAt,
       (SELECT TOP 1 vr.Status
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffStatus,
       (SELECT TOP 1 vr.RunTimestamp
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffAt,
       CAST(CASE WHEN EXISTS (
           SELECT 1 FROM dbo.ClientFavorites f
           WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
       ) THEN 1 ELSE 0 END AS BIT) AS IsFavorite,
       CAST(N'Admin' AS NVARCHAR(50)) AS CurrentUserEngagementRole
FROM dbo.Clients c
LEFT JOIN dbo.Users u ON u.UserID = c.CreatedBy
WHERE 1 = 1"
                    + (normalizedSearch != null
                        ? @"
  AND (
        LOWER(c.EngagementName) LIKE @Search
        OR LOWER(c.MaconomyNumber) LIKE @Search
        OR LOWER(ISNULL(c.Industry, '')) LIKE @Search
        OR LOWER(ISNULL(c.Status, '')) LIKE @Search
        OR LOWER(ISNULL(c.DirectorName, '')) LIKE @Search
        OR LOWER(ISNULL(c.ManagerName, '')) LIKE @Search
        OR LOWER(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, '')) LIKE @Search
    )"
                        : "") + @"
  AND (
        @Scope = 'all'
        OR (@Scope = 'active' AND c.Status IN ('Approved', 'Active'))
        OR (@Scope = 'archived' AND c.Status = 'Archived')
        OR (@Scope = 'favorites' AND EXISTS (
            SELECT 1 FROM dbo.ClientFavorites f
            WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
        ))
    )
ORDER BY c.CreatedAt DESC, c.ClientID DESC;"
                : isDirector
                ? @"
SELECT c.ClientID, c.EngagementName, c.MaconomyNumber, ISNULL(c.Industry, '') AS Industry, c.Status, c.CreatedAt,
       ISNULL(u.FirstName + ' ' + u.LastName, '') AS CreatedByName,
       (SELECT COUNT(1) FROM dbo.UserClientAssignments a WHERE a.ClientID = c.ClientID) AS AssignedUsersCount,
       (SELECT COUNT(1) FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID) AS ValidationRunsCount,
       (SELECT COUNT(1)
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )) AS SignedOffValidationRunsCount,
       (SELECT TOP 1 vr.RunID FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunId,
       (SELECT TOP 1 vr.RuleNumber FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunRuleNumber,
       (SELECT TOP 1 vr.RunID
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunId,
       (SELECT TOP 1 vr.RuleNumber
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunRuleNumber,
       (SELECT TOP 1 vr.Status FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunStatus,
       (SELECT TOP 1 vr.RunTimestamp FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunAt,
       (SELECT TOP 1 vr.Status
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffStatus,
       (SELECT TOP 1 vr.RunTimestamp
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffAt,
       CAST(CASE WHEN EXISTS (
           SELECT 1 FROM dbo.ClientFavorites f
           WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
       ) THEN 1 ELSE 0 END AS BIT) AS IsFavorite,
       ISNULL((SELECT TOP 1 a.EngagementRole FROM dbo.UserClientAssignments a WHERE a.ClientID = c.ClientID AND a.UserID = @UserID), '') AS CurrentUserEngagementRole
FROM dbo.Clients c
LEFT JOIN dbo.Users u ON u.UserID = c.CreatedBy
WHERE (
       c.CreatedBy = @UserID
       OR c.Status = 'Archived'
       OR EXISTS (
           SELECT 1 FROM dbo.UserClientAssignments a
           WHERE a.ClientID = c.ClientID AND a.UserID = @UserID
       )
   )"
                    + (normalizedSearch != null
                        ? @"
  AND (
        LOWER(c.EngagementName) LIKE @Search
        OR LOWER(c.MaconomyNumber) LIKE @Search
        OR LOWER(ISNULL(c.Industry, '')) LIKE @Search
        OR LOWER(ISNULL(c.Status, '')) LIKE @Search
        OR LOWER(ISNULL(c.DirectorName, '')) LIKE @Search
        OR LOWER(ISNULL(c.ManagerName, '')) LIKE @Search
        OR LOWER(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, '')) LIKE @Search
    )"
                        : "") + @"
  AND (
        @Scope = 'all'
        OR (@Scope = 'active' AND c.Status IN ('Approved', 'Active'))
        OR (@Scope = 'archived' AND c.Status = 'Archived')
        OR (@Scope = 'favorites' AND EXISTS (
            SELECT 1 FROM dbo.ClientFavorites f
            WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
        ))
    )
ORDER BY c.CreatedAt DESC, c.ClientID DESC;"
                : @"
SELECT c.ClientID, c.EngagementName, c.MaconomyNumber, ISNULL(c.Industry, '') AS Industry, c.Status, c.CreatedAt,
       ISNULL(u.FirstName + ' ' + u.LastName, '') AS CreatedByName,
       (SELECT COUNT(1) FROM dbo.UserClientAssignments a WHERE a.ClientID = c.ClientID) AS AssignedUsersCount,
       (SELECT COUNT(1) FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID) AS ValidationRunsCount,
       (SELECT COUNT(1)
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )) AS SignedOffValidationRunsCount,
       (SELECT TOP 1 vr.RunID FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunId,
       (SELECT TOP 1 vr.RuleNumber FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestRunRuleNumber,
       (SELECT TOP 1 vr.RunID
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunId,
       (SELECT TOP 1 vr.RuleNumber
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffRunRuleNumber,
       (SELECT TOP 1 vr.Status FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunStatus,
       (SELECT TOP 1 vr.RunTimestamp FROM dbo.ValidationRuns vr WHERE vr.ClientID = c.ClientID ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LastRunAt,
       (SELECT TOP 1 vr.Status
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffStatus,
       (SELECT TOP 1 vr.RunTimestamp
        FROM dbo.ValidationRuns vr
        WHERE vr.ClientID = c.ClientID
          AND EXISTS (
              SELECT 1 FROM dbo.ReviewSignoffs rs
              WHERE rs.RunID = vr.RunID AND rs.SignoffRole = 'DataAnalyst'
          )
        ORDER BY vr.RunTimestamp DESC, vr.RunID DESC) AS LatestSignedOffAt,
       CAST(CASE WHEN EXISTS (
           SELECT 1 FROM dbo.ClientFavorites f
           WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
       ) THEN 1 ELSE 0 END AS BIT) AS IsFavorite,
       ISNULL((SELECT TOP 1 a.EngagementRole FROM dbo.UserClientAssignments a WHERE a.ClientID = c.ClientID AND a.UserID = @UserID), '') AS CurrentUserEngagementRole
FROM dbo.Clients c
LEFT JOIN dbo.Users u ON u.UserID = c.CreatedBy
WHERE (
       c.Status = 'Archived'
       OR EXISTS (
           SELECT 1 FROM dbo.UserClientAssignments a
           WHERE a.ClientID = c.ClientID AND a.UserID = @UserID
       )
   )"
                    + (normalizedSearch != null
                        ? @"
  AND (
        LOWER(c.EngagementName) LIKE @Search
        OR LOWER(c.MaconomyNumber) LIKE @Search
        OR LOWER(ISNULL(c.Industry, '')) LIKE @Search
        OR LOWER(ISNULL(c.Status, '')) LIKE @Search
        OR LOWER(ISNULL(c.DirectorName, '')) LIKE @Search
        OR LOWER(ISNULL(c.ManagerName, '')) LIKE @Search
        OR LOWER(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, '')) LIKE @Search
    )"
                        : "") + @"
  AND (
        @Scope = 'all'
        OR (@Scope = 'active' AND c.Status IN ('Approved', 'Active'))
        OR (@Scope = 'archived' AND c.Status = 'Archived')
        OR (@Scope = 'favorites' AND EXISTS (
            SELECT 1 FROM dbo.ClientFavorites f
            WHERE f.ClientID = c.ClientID AND f.UserID = @UserID
        ))
    )
ORDER BY c.CreatedAt DESC, c.ClientID DESC;";
            command.Parameters.AddWithValue("@UserID", userId);
            command.Parameters.AddWithValue("@Scope", normalizedScope);
            if (normalizedSearch != null)
                command.Parameters.AddWithValue("@Search", normalizedSearch);

            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<ClientListViewModel>();
            while (await reader.ReadAsync())
            {
                var engagementName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var maconomyNumber = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var industry = reader.IsDBNull(3) || string.IsNullOrWhiteSpace(reader.GetString(3))
                    ? "Unspecified"
                    : reader.GetString(3);
                var status = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var createdAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5);
                var createdByName = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var assignedUsersCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                var validationRunsCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
                var signedOffValidationRunsCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);
                var latestRunId = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10);
                var latestRunRuleNumber = reader.IsDBNull(11) ? (int?)null : reader.GetInt32(11);
                var latestSignedOffRunId = reader.IsDBNull(12) ? (int?)null : reader.GetInt32(12);
                var latestSignedOffRunRuleNumber = reader.IsDBNull(13) ? (int?)null : reader.GetInt32(13);
                var lastRunStatus = reader.IsDBNull(14) ? null : reader.GetString(14);
                DateTime? lastRunAt = reader.IsDBNull(15) ? (DateTime?)null : reader.GetDateTime(15);
                var latestSignedOffStatus = reader.IsDBNull(16) ? null : reader.GetString(16);
                DateTime? latestSignedOffAt = reader.IsDBNull(17) ? (DateTime?)null : reader.GetDateTime(17);
                var isFavorite = !reader.IsDBNull(18) && reader.GetBoolean(18);
                var currentUserEngagementRole = reader.IsDBNull(19) ? "" : reader.GetString(19);
                list.Add(new ClientListViewModel
                {
                    Id = reader.GetInt32(0),
                    Name = engagementName,
                    FiscalYear = maconomyNumber,
                    EngagementName = engagementName,
                    MaconomyNumber = maconomyNumber,
                    Industry = industry,
                    Status = status,
                    CreatedAt = createdAt,
                    CreatedByName = createdByName,
                    AssignedUsersCount = assignedUsersCount,
                    ValidationRunsCount = validationRunsCount,
                    LatestRunId = latestRunId,
                    LatestRunRuleNumber = latestRunRuleNumber,
                    LatestSignedOffRunId = latestSignedOffRunId,
                    LatestSignedOffRunRuleNumber = latestSignedOffRunRuleNumber,
                    LastRunStatus = lastRunStatus,
                    LastRunAt = lastRunAt,
                    LatestSignedOffStatus = latestSignedOffStatus,
                    LatestSignedOffAt = latestSignedOffAt,
                    CurrentUserEngagementRole = currentUserEngagementRole
                    ,
                    IsFavorite = isFavorite
                });
            }

            return list;
        }

        private async Task<List<ClientUserRow>> GetAssignedUsersAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT a.AssignmentID, u.UserID, ISNULL(u.FirstName,'') + CASE WHEN ISNULL(u.LastName,'') = '' THEN '' ELSE ' ' + u.LastName END AS FullName,
       ISNULL(u.Email,'') AS Email, ISNULL(a.EngagementRole,'DataAnalyst') AS EngagementRole, ISNULL(a.AssignedBy,0) AS AssignedBy, a.AssignedAt
FROM dbo.UserClientAssignments a
INNER JOIN dbo.Users u ON u.UserID = a.UserID
WHERE a.ClientID = @ClientID
ORDER BY u.FirstName, u.LastName;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<ClientUserRow>();
            while (await reader.ReadAsync())
            {
                list.Add(new ClientUserRow
                {
                    ClientUserId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1).ToString(),
                    FullName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Email = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    EngagementRole = reader.IsDBNull(4) ? "DataAnalyst" : reader.GetString(4),
                    AssignedAt = reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6),
                    IsActive = true
                });
            }
            return list;
        }

        private async Task<List<ValidationRunRow>> GetValidationRunsForClientAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.RuleNumber, vr.RuleName, vr.Status, vr.TotalRecords, vr.PassCount, vr.FailCount, vr.ExceptionRate, vr.RunTimestamp,
       COALESCE(NULLIF(vr.RunByUserName,''), LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,'')))) AS RunByUserName,
       ISNULL(vr.LastEditedByUserName,'') AS LastEditedByUserName,
       vr.LastEditedAt,
       vr.IsCurrent,
       (SELECT COUNT(1) FROM dbo.ReviewSignoffs rs WHERE rs.RunID = vr.RunID) AS SignoffCount,
       CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.ReviewSignoffs rs
            WHERE rs.RunID = vr.RunID
              AND ISNULL(rs.SignoffRole, '') = 'DataAnalyst'
       ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDataAnalystSignoff,
       CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.ReviewSignoffs rs
            WHERE rs.RunID = vr.RunID
              AND ISNULL(rs.SignoffRole, '') = 'Manager'
       ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasManagerSignoff,
       CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.ReviewSignoffs rs
            WHERE rs.RunID = vr.RunID
              AND ISNULL(rs.SignoffRole, '') = 'Director'
       ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasDirectorSignoff
FROM dbo.ValidationRuns vr
LEFT JOIN dbo.Users u ON u.UserID = vr.UserID
WHERE vr.ClientID = @ClientID
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<ValidationRunRow>();
            while (await reader.ReadAsync())
            {
                list.Add(new ValidationRunRow
                {
                    Id = reader.GetInt32(0),
                    RuleNumber = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    RuleName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TotalValidated = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    PassCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    FailCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    ExceptionRate = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                    RunAt = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8),
                    RunByUserName = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    LastEditedByUserName = reader.IsDBNull(10) ? null : reader.GetString(10),
                    LastEditedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    IsCurrent = !reader.IsDBNull(12) && reader.GetBoolean(12),
                    SignoffCount = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                    HasDataAnalystSignoff = !reader.IsDBNull(14) && reader.GetBoolean(14),
                    HasManagerSignoff = !reader.IsDBNull(15) && reader.GetBoolean(15),
                    HasDirectorSignoff = !reader.IsDBNull(16) && reader.GetBoolean(16)
                });
            }
            return list;
        }

        private async Task<List<MessageItemViewModel>> GetThreadMessagesAsync(SqlConnection connection, int threadId, int currentUserId)
        {
            var attachmentsByMessage = await GetMessageAttachmentsAsync(connection, threadId);
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT tm.MessageID,
       tm.ThreadID,
       LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS SenderName,
       ISNULL(u.Email,'') AS SenderEmail,
       tm.Body,
       tm.SentAt,
       CASE WHEN tm.SenderUserID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser,
       CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
              AND r.UserID = @CurrentUserID
              AND r.IsRead = 1
       ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsRead,
       (
            SELECT TOP 1 r.ReadAt
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
              AND r.UserID = @CurrentUserID
       ) AS ReadAt
       ,
       (
            SELECT COUNT(1)
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
       ) AS RecipientCount,
       (
            SELECT COUNT(1)
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
              AND r.IsRead = 1
       ) AS ReadCount,
       (
            SELECT MIN(r.ReadAt)
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
              AND r.IsRead = 1
       ) AS FirstReadAt,
       (
            SELECT MAX(r.ReadAt)
            FROM dbo.ThreadMessageRecipients r
            WHERE r.MessageID = tm.MessageID
              AND r.IsRead = 1
       ) AS LastReadAt,
       CASE WHEN tm.SenderUserID = @CurrentUserID AND ISNULL(tm.IsDeleted, 0) = 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS CanEdit,
       CASE WHEN tm.SenderUserID = @CurrentUserID AND ISNULL(tm.IsDeleted, 0) = 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS CanDelete,
       CASE WHEN tm.EditedAt IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS IsEdited,
       tm.EditedAt,
       ISNULL(tm.IsDeleted, 0) AS IsDeleted,
       tm.DeletedAt
FROM dbo.ThreadMessages tm
INNER JOIN dbo.Users u ON u.UserID = tm.SenderUserID
WHERE tm.ThreadID = @ThreadID
ORDER BY tm.SentAt ASC, tm.MessageID ASC;";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId);
            await using var reader = await command.ExecuteReaderAsync();
            var messages = new List<MessageItemViewModel>();
            while (await reader.ReadAsync())
            {
                messages.Add(new MessageItemViewModel
                {
                    MessageId = reader.GetInt32(0),
                    ThreadId = reader.GetInt32(1),
                    SenderName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SenderEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Body = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SentAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6),
                    IsRead = !reader.IsDBNull(7) && reader.GetBoolean(7),
                    ReadAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    RecipientCount = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetInt32(9) : 0,
                    ReadCount = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetInt32(10) : 0,
                    FirstReadAt = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetDateTime(11) : null,
                    LastReadAt = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetDateTime(12) : null,
                    CanEdit = reader.FieldCount > 13 && !reader.IsDBNull(13) && reader.GetBoolean(13),
                    CanDelete = reader.FieldCount > 14 && !reader.IsDBNull(14) && reader.GetBoolean(14),
                    IsEdited = reader.FieldCount > 15 && !reader.IsDBNull(15) && reader.GetBoolean(15),
                    EditedAt = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetDateTime(16) : null,
                    IsDeleted = reader.FieldCount > 17 && !reader.IsDBNull(17) && reader.GetBoolean(17),
                    DeletedAt = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetDateTime(18) : null,
                    Attachments = attachmentsByMessage.TryGetValue(reader.GetInt32(0), out var messageAttachments)
                        ? messageAttachments
                        : new List<MessageAttachmentViewModel>()
                });
            }

            return messages;
        }

        private async Task<List<int>> GetThreadParticipantIdsAsync(SqlConnection connection, int threadId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT DISTINCT participant.UserID
FROM (
    SELECT tm.SenderUserID AS UserID
    FROM dbo.ThreadMessages tm
    WHERE tm.ThreadID = @ThreadID
    UNION
    SELECT r.UserID
    FROM dbo.ThreadMessages tm
    INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
    WHERE tm.ThreadID = @ThreadID
) participant;";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            await using var reader = await command.ExecuteReaderAsync();
            var list = new List<int>();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    list.Add(reader.GetInt32(0));
            }

            return list;
        }

        private async Task<Dictionary<int, List<MessageAttachmentViewModel>>> GetMessageAttachmentsAsync(SqlConnection connection, int threadId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT att.AttachmentID,
       att.MessageID,
       att.FileName,
       att.FilePath,
       att.ContentType,
       att.FileSize,
       att.AttachmentKind
FROM dbo.ThreadMessageAttachments att
INNER JOIN dbo.ThreadMessages tm ON tm.MessageID = att.MessageID
WHERE tm.ThreadID = @ThreadID
ORDER BY att.AttachmentID ASC;";
            command.Parameters.AddWithValue("@ThreadID", threadId);

            await using var reader = await command.ExecuteReaderAsync();
            var attachments = new Dictionary<int, List<MessageAttachmentViewModel>>();
            while (await reader.ReadAsync())
            {
                var messageId = reader.GetInt32(1);
                if (!attachments.TryGetValue(messageId, out var list))
                {
                    list = new List<MessageAttachmentViewModel>();
                    attachments[messageId] = list;
                }

                list.Add(new MessageAttachmentViewModel
                {
                    AttachmentId = reader.GetInt32(0),
                    MessageId = messageId,
                    FileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    FilePath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ContentType = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    FileSize = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    AttachmentKind = reader.IsDBNull(6) ? "file" : reader.GetString(6)
                });
            }

            return attachments;
        }

        private async Task<bool> CanAccessThreadAsync(SqlConnection connection, int threadId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM dbo.MessageThreads th
WHERE th.ThreadID = @ThreadID
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.ThreadUserStates tus
      WHERE tus.ThreadID = th.ThreadID
        AND tus.UserID = @UserID
        AND tus.IsDeleted = 1
  )
  AND (
      th.CreatedByUserID = @UserID
      OR EXISTS (
          SELECT 1
          FROM dbo.ThreadMessages tm
          INNER JOIN dbo.ThreadMessageRecipients r ON r.MessageID = tm.MessageID
          WHERE tm.ThreadID = th.ThreadID
            AND r.UserID = @UserID
      )
      OR EXISTS (
          SELECT 1
          FROM dbo.ThreadMessages tm
          WHERE tm.ThreadID = th.ThreadID
            AND tm.SenderUserID = @UserID
      )
  );";
            command.Parameters.AddWithValue("@ThreadID", threadId);
            command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private async Task<bool> CanAccessMessageAsync(SqlConnection connection, int messageId, int threadId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM dbo.ThreadMessages tm
WHERE tm.MessageID = @MessageID
  AND tm.ThreadID = @ThreadID
  AND ISNULL(tm.IsDeleted, 0) = 0
  AND tm.SenderUserID = @UserID;";
            command.Parameters.AddWithValue("@MessageID", messageId);
            command.Parameters.AddWithValue("@ThreadID", threadId);
            command.Parameters.AddWithValue("@UserID", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private async Task RestoreThreadForUsersAsync(SqlConnection connection, int threadId, IEnumerable<int> userIds)
        {
            foreach (var participantId in userIds.Where(id => id > 0).Distinct())
            {
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
MERGE dbo.ThreadUserStates AS target
USING (SELECT @ThreadID AS ThreadID, @UserID AS UserID) AS source
    ON target.ThreadID = source.ThreadID AND target.UserID = source.UserID
WHEN MATCHED THEN
    UPDATE SET IsDeleted = 0, DeletedAt = NULL
WHEN NOT MATCHED THEN
    INSERT (ThreadID, UserID, IsDeleted, DeletedAt)
    VALUES (@ThreadID, @UserID, 0, NULL);";
                command.Parameters.AddWithValue("@ThreadID", threadId);
                command.Parameters.AddWithValue("@UserID", participantId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task<int> InsertThreadMessageAsync(
            SqlConnection connection,
            int threadId,
            int senderUserId,
            string body,
            int? replyToMessageId,
            IEnumerable<int> recipientUserIds,
            DateTime sentAt,
            IEnumerable<MessageAttachmentInput>? attachments = null)
        {
            var previousHash = await GetLatestHashAsync(connection, "dbo.ThreadMessages");

            await using var insert = connection.CreateConfiguredCommand();
            insert.CommandText = @"
INSERT INTO dbo.ThreadMessages (ThreadID, SenderUserID, Body, ReplyToMessageID, SentAt, PreviousHash, RecordHash, EditedAt, IsDeleted, DeletedAt)
VALUES (@ThreadID, @SenderUserID, @Body, @ReplyToMessageID, @SentAt, @PreviousHash, NULL, NULL, 0, NULL);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            insert.Parameters.AddWithValue("@ThreadID", threadId);
            insert.Parameters.AddWithValue("@SenderUserID", senderUserId);
            insert.Parameters.AddWithValue("@Body", body);
            insert.Parameters.AddWithValue("@ReplyToMessageID", (object?)replyToMessageId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@SentAt", sentAt);
            insert.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            var messageId = Convert.ToInt32(await insert.ExecuteScalarAsync());

            var recordHash = ComputeHash($@"ThreadMessage|{messageId}|{threadId}|{senderUserId}|{body}|{sentAt:o}|{replyToMessageId}|{previousHash}");
            await using (var update = connection.CreateConfiguredCommand())
            {
                update.CommandText = "UPDATE dbo.ThreadMessages SET RecordHash = @RecordHash WHERE MessageID = @MessageID;";
                update.Parameters.AddWithValue("@RecordHash", recordHash);
                update.Parameters.AddWithValue("@MessageID", messageId);
                await update.ExecuteNonQueryAsync();
            }

            foreach (var recipientId in recipientUserIds.Distinct())
            {
                await using var recipient = connection.CreateConfiguredCommand();
                recipient.CommandText = @"
INSERT INTO dbo.ThreadMessageRecipients (MessageID, UserID, IsRead, ReadAt)
VALUES (@MessageID, @UserID, 0, NULL);";
                recipient.Parameters.AddWithValue("@MessageID", messageId);
                recipient.Parameters.AddWithValue("@UserID", recipientId);
                await recipient.ExecuteNonQueryAsync();
            }

            if (attachments != null)
            {
                foreach (var attachment in attachments.Where(item => item != null))
                {
                    await using var attachmentCommand = connection.CreateConfiguredCommand();
                    attachmentCommand.CommandText = @"
INSERT INTO dbo.ThreadMessageAttachments (MessageID, FileName, FilePath, ContentType, FileSize, AttachmentKind, CreatedAt)
VALUES (@MessageID, @FileName, @FilePath, @ContentType, @FileSize, @AttachmentKind, @CreatedAt);";
                    attachmentCommand.Parameters.AddWithValue("@MessageID", messageId);
                    attachmentCommand.Parameters.AddWithValue("@FileName", attachment.FileName);
                    attachmentCommand.Parameters.AddWithValue("@FilePath", attachment.FilePath);
                    attachmentCommand.Parameters.AddWithValue("@ContentType", attachment.ContentType);
                    attachmentCommand.Parameters.AddWithValue("@FileSize", attachment.FileSize);
                    attachmentCommand.Parameters.AddWithValue("@AttachmentKind", attachment.AttachmentKind);
                    attachmentCommand.Parameters.AddWithValue("@CreatedAt", sentAt);
                    await attachmentCommand.ExecuteNonQueryAsync();
                }
            }

            return messageId;
        }

        private async Task<int?> ResolveAuditUserIdAsync(SqlConnection connection, string? userId, string? userName)
        {
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var byEmail = await GetUserIdByEmailAsync(connection, userName);
                if (byEmail.HasValue)
                    return byEmail;
            }

            if (int.TryParse(userId, out var parsed))
            {
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE UserID = @UserID;";
                command.Parameters.AddWithValue("@UserID", parsed);
                var value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
            }

            return null;
        }

        private async Task<string?> GetLatestHashAsync(SqlConnection connection, string tableName)
        {
            var sql = tableName switch
            {
                "dbo.AuditLog" => "SELECT TOP 1 RecordHash FROM dbo.AuditLog WHERE RecordHash IS NOT NULL ORDER BY LogID DESC;",
                "dbo.ValidationRuns" => "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RecordHash IS NOT NULL ORDER BY RunID DESC;",
                "dbo.MessageThreads" => "SELECT TOP 1 RecordHash FROM dbo.MessageThreads WHERE RecordHash IS NOT NULL ORDER BY ThreadID DESC;",
                "dbo.ThreadMessages" => "SELECT TOP 1 RecordHash FROM dbo.ThreadMessages WHERE RecordHash IS NOT NULL ORDER BY MessageID DESC;",
                _ => $"SELECT TOP 1 RecordHash FROM {tableName} WHERE RecordHash IS NOT NULL;"
            };

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 Status
FROM dbo.Clients
WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private async Task EnsureAssignmentClientNotArchivedAsync(SqlConnection connection, int clientUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 c.Status
FROM dbo.UserClientAssignments a
INNER JOIN dbo.Clients c ON c.ClientID = a.ClientID
WHERE a.AssignmentID = @AssignmentID;";
            command.Parameters.AddWithValue("@AssignmentID", clientUserId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private static string FormatMissingSignoffMessage(IReadOnlyList<string> missing)
        {
            if (missing.Count == 0)
                return "review signoffs";

            if (missing.Count == 1)
                return $"{missing[0]} signoff";

            if (missing.Count == 2)
                return $"{missing[0]} and {missing[1]} signoffs";

            return $"{string.Join(", ", missing.Take(missing.Count - 1))}, and {missing[^1]} signoffs";
        }

        private static string BuildArchiveEligibilityMessage(IReadOnlyList<ValidationRunRow> incompleteCurrentRuns)
        {
            if (incompleteCurrentRuns.Count == 0)
                return "All current results must be reviewed and completed before archiving.";

            var labels = incompleteCurrentRuns
                .Select(run => $"Rule {run.RuleNumber} ({run.Status})")
                .ToList();

            return $"Archive is locked until every current result shows Reviewed and Completed. Outstanding rules: {string.Join(", ", labels)}.";
        }

        private async Task<(int UserId, bool IsAdmin)> ResolveUserScopeAsync(ApplicationUser? user, string role)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
                return (0, false);

            var userId = await EnsureUserMirrorAsync(user, role);
            return (userId, string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<int?> GetUserIdByEmailAsync(SqlConnection connection, string email)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<int?> ResolveUserIdByEmailAsync(string email)
        {
            await using var connection = await OpenConnectionAsync();
            return await GetUserIdByEmailAsync(connection, email);
        }

        private async Task<SqlConnection> OpenConnectionAsync()
        {
            var server = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = trust,
                Encrypt = false,
                ConnectTimeout = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private async Task<(bool CanAccess, string CurrentUserEngagementRole)> GetClientResultsAccessAsync(int clientId, ApplicationUser? user, string role)
        {
            var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            var isDirector = string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            var userId = 0;

            if (!isAdmin)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                    return (false, "");

                userId = await EnsureUserMirrorAsync(user, role);
            }

            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = 30;
            command.CommandText = isAdmin
                ? @"
SELECT
    CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.Clients c
        WHERE c.ClientID = @ClientID
          AND c.Status IN ('Approved', 'Active', 'Archived')
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS CanAccess,
    CAST(N'Admin' AS nvarchar(50)) AS CurrentUserEngagementRole;"
                : isDirector
                    ? @"
SELECT
    CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.Clients c
        WHERE c.ClientID = @ClientID
          AND c.Status IN ('Approved', 'Active', 'Archived')
          AND (
              c.CreatedBy = @UserID
              OR c.Status = 'Archived'
              OR EXISTS (
                  SELECT 1
                  FROM dbo.UserClientAssignments a
                  WHERE a.ClientID = c.ClientID
                    AND a.UserID = @UserID
              )
          )
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS CanAccess,
    ISNULL((
        SELECT TOP 1 a.EngagementRole
        FROM dbo.UserClientAssignments a
        WHERE a.ClientID = @ClientID
          AND a.UserID = @UserID
    ), '') AS CurrentUserEngagementRole;"
                    : @"
SELECT
    CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.Clients c
        WHERE c.ClientID = @ClientID
          AND c.Status IN ('Approved', 'Active', 'Archived')
          AND (
              c.Status = 'Archived'
              OR EXISTS (
                  SELECT 1
                  FROM dbo.UserClientAssignments a
                  WHERE a.ClientID = c.ClientID
                    AND a.UserID = @UserID
              )
          )
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS CanAccess,
    ISNULL((
        SELECT TOP 1 a.EngagementRole
        FROM dbo.UserClientAssignments a
        WHERE a.ClientID = @ClientID
          AND a.UserID = @UserID
    ), '') AS CurrentUserEngagementRole;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            if (!isAdmin)
                command.Parameters.AddWithValue("@UserID", userId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, "");

            return (!reader.IsDBNull(0) && reader.GetBoolean(0), reader.IsDBNull(1) ? "" : reader.GetString(1));
        }
    }
}
