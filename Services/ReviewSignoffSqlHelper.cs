using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace HemisAudit.Services
{
    internal static class ReviewSignoffSqlHelper
    {
        internal sealed record SignoffRemovalVersioningResult(
            int RemovedCount,
            string? SignoffRole,
            bool HistoricalSnapshotPreserved,
            int HistoricalRunId,
            int? NewCurrentRunId);

        public static async Task<(int RemovedCount, string? SignoffRole)> RemoveReviewerSignoffAsync(
            SqlConnection connection,
            int runId,
            int reviewerId)
        {
            var signoffRole = await GetReviewerSignoffRoleAsync(connection, runId, reviewerId);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
DELETE FROM dbo.ReviewSignoffs
WHERE RunID = @RunID
  AND ReviewerID = @ReviewerID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);

            var removedCount = await command.ExecuteNonQueryAsync();
            return (removedCount, signoffRole);
        }

        public static async Task<SignoffRemovalVersioningResult> RemoveReviewerSignoffWithVersioningAsync(
            SqlConnection connection,
            int runId,
            int reviewerId)
        {
            var snapshot = await GetReviewerRunSnapshotAsync(connection, runId, reviewerId);
            if (snapshot == null)
            {
                return new SignoffRemovalVersioningResult(0, null, false, runId, null);
            }

            if (!snapshot.IsCurrent)
            {
                var removal = await RemoveReviewerSignoffAsync(connection, runId, reviewerId);
                if (removal.RemovedCount > 0)
                {
                    await SetRunStatusAsync(connection, runId, await DetermineRunStatusAsync(connection, runId));
                }

                return new SignoffRemovalVersioningResult(
                    removal.RemovedCount,
                    removal.SignoffRole,
                    false,
                    runId,
                    null);
            }

            var reviewerDisplayName = await GetReviewerDisplayNameAsync(connection, reviewerId);
            var removedRank = GetRoleRank(snapshot.SignoffRole);

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            try
            {
                await SetRunCurrentStateAsync(connection, transaction, runId, false);

                var newRunId = await CloneValidationRunAsync(
                    connection,
                    transaction,
                    runId,
                    snapshot.RecordHash,
                    reviewerDisplayName);

                if (removedRank > 1)
                {
                    await CopyRetainedSignoffsAsync(connection, transaction, runId, newRunId, removedRank);
                }

                await SetRunStatusAsync(connection, transaction, newRunId, "Needs Review");
                await SetRunRecordHashAsync(connection, transaction, runId, newRunId, reviewerId, snapshot.SignoffRole, snapshot.RecordHash);

                await transaction.CommitAsync();

                return new SignoffRemovalVersioningResult(
                    1,
                    snapshot.SignoffRole,
                    true,
                    runId,
                    newRunId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<string?> GetReviewerSignoffRoleAsync(SqlConnection connection, int runId, int reviewerId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP (1) SignoffRole
FROM dbo.ReviewSignoffs
WHERE RunID = @RunID
  AND ReviewerID = @ReviewerID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private sealed record ReviewerRunSnapshot(string SignoffRole, bool IsCurrent, string? RecordHash);

        private static async Task<ReviewerRunSnapshot?> GetReviewerRunSnapshotAsync(SqlConnection connection, int runId, int reviewerId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP (1)
       ISNULL(rs.SignoffRole, '') AS SignoffRole,
       CAST(ISNULL(vr.IsCurrent, 0) AS bit) AS IsCurrent,
       vr.RecordHash
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.ValidationRuns vr ON vr.RunID = rs.RunID
WHERE rs.RunID = @RunID
  AND rs.ReviewerID = @ReviewerID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new ReviewerRunSnapshot(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                !reader.IsDBNull(1) && reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        private static async Task<string?> GetReviewerDisplayNameAsync(SqlConnection connection, int reviewerId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP (1)
       NULLIF(LTRIM(RTRIM(ISNULL(FirstName, '') + ' ' + ISNULL(LastName, ''))), '')
FROM dbo.Users
WHERE UserID = @ReviewerID;";
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);

            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToString(result);
        }

        private static async Task<int> CloneValidationRunAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int sourceRunId,
            string? previousHash,
            string? reviewerDisplayName)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID, UserID, HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
 RuleNumber, RuleName, Status, RunTimestamp, TotalRecords, PassCount, FailCount, ExceptionRate,
 ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent)
SELECT
    ClientID,
    UserID,
    HemisServer,
    AuditDatabase,
    StudTable,
    DeceasedTable,
    StudColumn,
    DeceasedColumn,
    RuleNumber,
    RuleName,
    'Needs Review',
    GETDATE(),
    TotalRecords,
    PassCount,
    FailCount,
    ExceptionRate,
    ExceptionsJSON,
    ResultsJSON,
    RunByUserName,
    COALESCE(@ReviewerDisplayName, LastEditedByUserName),
    GETDATE(),
    @PreviousHash,
    NULL,
    1
FROM dbo.ValidationRuns
WHERE RunID = @SourceRunID;
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            command.Parameters.AddWithValue("@SourceRunID", sourceRunId);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReviewerDisplayName", (object?)reviewerDisplayName ?? DBNull.Value);

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static async Task CopyRetainedSignoffsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int sourceRunId,
            int newRunId,
            int removedRank)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
SELECT ClientID, @NewRunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt
FROM dbo.ReviewSignoffs
WHERE RunID = @SourceRunID
  AND CASE ISNULL(SignoffRole, '')
        WHEN 'DataAnalyst' THEN 1
        WHEN 'Manager' THEN 2
        WHEN 'Director' THEN 3
        ELSE 99
      END < @RemovedRank;";
            command.Parameters.AddWithValue("@NewRunID", newRunId);
            command.Parameters.AddWithValue("@SourceRunID", sourceRunId);
            command.Parameters.AddWithValue("@RemovedRank", removedRank);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string> DetermineRunStatusAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director') THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return "Needs Review";
            }

            var hasDataAnalyst = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
            var hasManager = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
            var hasDirector = !reader.IsDBNull(2) && reader.GetInt32(2) == 1;
            return hasDataAnalyst && hasManager && hasDirector
                ? "Reviewed and Completed"
                : "Needs Review";
        }

        private static async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SetRunStatusAsync(SqlConnection connection, SqlTransaction transaction, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SetRunCurrentStateAsync(SqlConnection connection, SqlTransaction transaction, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = @IsCurrent WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SetRunRecordHashAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int historicalRunId,
            int newRunId,
            int reviewerId,
            string? signoffRole,
            string? previousHash)
        {
            var timestamp = DateTime.UtcNow;
            var recordHash = ComputeHash($"SignoffRemovalVersion|{historicalRunId}|{newRunId}|{reviewerId}|{signoffRole}|{timestamp:o}|{previousHash}");

            await using var command = connection.CreateConfiguredCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", newRunId);
            command.Parameters.AddWithValue("@RecordHash", recordHash);
            await command.ExecuteNonQueryAsync();
        }

        private static int GetRoleRank(string? signoffRole) => signoffRole switch
        {
            "DataAnalyst" => 1,
            "Manager" => 2,
            "Director" => 3,
            _ => 99
        };

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
