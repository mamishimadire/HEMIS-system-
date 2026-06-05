using Microsoft.Data.SqlClient;

namespace HemisAudit.Services
{
    internal static class SqlLargeDataExtensions
    {
        public const int LargeDataCommandTimeoutSeconds = 0;
        public const int LargeDataConnectionTimeoutSeconds = 180;

        public static SqlCommand CreateConfiguredCommand(this SqlConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandTimeout = LargeDataCommandTimeoutSeconds;
            return command;
        }

        public static SqlCommand WithLargeDataTimeout(this SqlCommand command)
        {
            command.CommandTimeout = LargeDataCommandTimeoutSeconds;
            return command;
        }
    }
}
