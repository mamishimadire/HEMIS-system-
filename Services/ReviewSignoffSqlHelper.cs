using Microsoft.Data.SqlClient;

namespace HemisAudit.Services
{
    internal static class ReviewSignoffSqlHelper
    {
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
    }
}
