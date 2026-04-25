using BCrypt.Net;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Data
{
    public static class SystemDatabaseBootstrapper
    {
        public static async Task EnsureCreatedAsync(IConfiguration configuration)
        {
            var server = configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trustServerCertificate = configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            await EnsureDatabaseExistsAsync(server, database, trustServerCertificate);

            var connectionString = BuildConnectionString(server, database, trustServerCertificate);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await ExecuteSchemaAsync(connection);
            await SeedAdminAsync(connection);
        }

        private static async Task EnsureDatabaseExistsAsync(string server, string database, bool trustServerCertificate)
        {
            var masterConnectionString = BuildConnectionString(server, "master", trustServerCertificate);
            using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF DB_ID(N'{EscapeSqlLiteral(database)}') IS NULL
BEGIN
    CREATE DATABASE [{database}];
END";
            await command.ExecuteNonQueryAsync();
        }

        private static async Task ExecuteSchemaAsync(SqlConnection connection)
        {
            var ddlStatements = new[]
            {
                @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
      UserID            INT IDENTITY(1,1) PRIMARY KEY,
      FirstName         NVARCHAR(100) NOT NULL,
      LastName          NVARCHAR(100) NOT NULL,
      Email             NVARCHAR(255) NOT NULL UNIQUE,
      EmployeeCode      NVARCHAR(50) NULL,
      PasswordHash      NVARCHAR(255) NOT NULL,
      SystemRole        NVARCHAR(50) NOT NULL CHECK (SystemRole IN ('Admin','Director','Manager','DataAnalyst','Trainee')),
      IsActive          BIT NOT NULL DEFAULT 1,
      MustResetPassword BIT NOT NULL DEFAULT 1,
      PasswordSetDate   DATETIME NOT NULL DEFAULT GETDATE(),
      PasswordHistory   NVARCHAR(MAX) NULL,
      CreatedAt         DATETIME NOT NULL DEFAULT GETDATE(),
      CreatedBy         INT NULL
    );
END",
                @"
IF OBJECT_ID(N'dbo.Clients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Clients (
      ClientID        INT IDENTITY(1,1) PRIMARY KEY,
      EngagementName  NVARCHAR(255) NOT NULL,
      MaconomyNumber  NVARCHAR(100) NOT NULL UNIQUE,
      Industry        NVARCHAR(100) NULL,
      DirectorName    NVARCHAR(200) NOT NULL,
      DirectorEmail   NVARCHAR(255) NOT NULL,
      DirectorEmpCode NVARCHAR(50)  NOT NULL,
      ManagerName     NVARCHAR(200) NOT NULL,
      ManagerEmail    NVARCHAR(255) NOT NULL,
      ManagerEmpCode  NVARCHAR(50)  NOT NULL,
      Status          NVARCHAR(50) NOT NULL DEFAULT 'Pending',
      CreatedBy       INT NOT NULL,
      ApprovedBy      INT NULL,
      ApprovedAt      DATETIME NULL,
      ArchivedBy      INT NULL,
      ArchivedAt      DATETIME NULL,
      CreatedAt       DATETIME NOT NULL DEFAULT GETDATE()
    );
END",
                @"
IF OBJECT_ID(N'dbo.UserClientAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserClientAssignments (
      AssignmentID INT IDENTITY(1,1) PRIMARY KEY,
      UserID       INT NOT NULL REFERENCES dbo.Users(UserID),
      ClientID     INT NOT NULL REFERENCES dbo.Clients(ClientID),
      EngagementRole NVARCHAR(50) NOT NULL DEFAULT 'DataAnalyst',
      AssignedBy   INT NOT NULL REFERENCES dbo.Users(UserID),
      AssignedAt   DATETIME NOT NULL DEFAULT GETDATE(),
      CONSTRAINT UQ_UserClientAssignments_User_Client UNIQUE (UserID, ClientID)
    );
END",
                @"
IF OBJECT_ID(N'dbo.ValidationRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ValidationRuns (
      RunID         INT IDENTITY(1,1) PRIMARY KEY,
      ClientID      INT NOT NULL REFERENCES dbo.Clients(ClientID),
      UserID        INT NOT NULL REFERENCES dbo.Users(UserID),
      HemisServer   NVARCHAR(255) NULL,
      AuditDatabase NVARCHAR(255) NOT NULL,
      StudTable     NVARCHAR(255) NULL,
      DeceasedTable NVARCHAR(255) NULL,
      StudColumn    NVARCHAR(255) NULL,
      DeceasedColumn NVARCHAR(255) NULL,
      RuleNumber    INT NULL,
      RuleName      NVARCHAR(255) NULL,
      Status        NVARCHAR(50) NULL,
      RunTimestamp  DATETIME NOT NULL DEFAULT GETDATE(),
      TotalRecords  INT NULL,
      PassCount     INT NULL,
      FailCount     INT NULL,
      ExceptionRate DECIMAL(10,4) NULL,
      ExceptionsJSON NVARCHAR(MAX) NULL,
      ResultsJSON   NVARCHAR(MAX) NULL,
      RunByUserName NVARCHAR(255) NULL,
      LastEditedByUserName NVARCHAR(255) NULL,
      LastEditedAt  DATETIME NULL,
      PreviousHash  NVARCHAR(128) NULL,
      RecordHash    NVARCHAR(128) NULL,
      IsCurrent     BIT NOT NULL DEFAULT 1
    );
END",
                @"
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog (
      LogID       INT IDENTITY(1,1) PRIMARY KEY,
      UserID      INT NULL REFERENCES dbo.Users(UserID),
      Action      NVARCHAR(255) NOT NULL,
      EntityType  NVARCHAR(100) NULL,
      EntityID    INT NULL,
      OldValues   NVARCHAR(MAX) NULL,
      NewValues   NVARCHAR(MAX) NULL,
      IPAddress   NVARCHAR(50) NULL,
      Timestamp   DATETIME NOT NULL DEFAULT GETDATE(),
      PreviousHash NVARCHAR(128) NULL,
      RecordHash   NVARCHAR(128) NULL
    );
END",
                @"
IF OBJECT_ID(N'dbo.ReviewSignoffs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReviewSignoffs (
      SignoffID   INT IDENTITY(1,1) PRIMARY KEY,
      ClientID    INT NOT NULL REFERENCES dbo.Clients(ClientID),
      RunID       INT NULL REFERENCES dbo.ValidationRuns(RunID),
      ReviewerID  INT NOT NULL REFERENCES dbo.Users(UserID),
      SignoffRole NVARCHAR(50) NOT NULL DEFAULT 'DataAnalyst',
      ReviewType  NVARCHAR(50) NOT NULL CHECK (ReviewType IN ('Preliminary','Final')),
      Comment     NVARCHAR(MAX) NULL,
      SignedOffAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END",
                @"
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PasswordResetTokens (
      TokenID   INT IDENTITY(1,1) PRIMARY KEY,
      UserID    INT NOT NULL REFERENCES dbo.Users(UserID),
      Token     NVARCHAR(500) NOT NULL UNIQUE,
      ExpiresAt DATETIME NOT NULL,
      Used      BIT NOT NULL DEFAULT 0,
      CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END",
                @"
IF OBJECT_ID(N'dbo.ImpersonationLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImpersonationLog (
      ImpersonationID    INT IDENTITY(1,1) PRIMARY KEY,
      AdminUserID        INT NOT NULL REFERENCES dbo.Users(UserID),
      ImpersonatedRole   NVARCHAR(50) NOT NULL,
      ImpersonatedUserID INT NULL REFERENCES dbo.Users(UserID),
      StartedAt          DATETIME NOT NULL DEFAULT GETDATE(),
      EndedAt            DATETIME NULL
    );
END",
                @"
IF OBJECT_ID(N'dbo.MessageThreads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MessageThreads (
      ThreadID     INT IDENTITY(1,1) PRIMARY KEY,
      ClientID     INT NULL REFERENCES dbo.Clients(ClientID),
      Subject      NVARCHAR(255) NOT NULL,
      CreatedByUserID INT NOT NULL REFERENCES dbo.Users(UserID),
      CreatedAt    DATETIME NOT NULL DEFAULT GETDATE(),
      LastMessageAt DATETIME NOT NULL DEFAULT GETDATE(),
      PreviousHash NVARCHAR(128) NULL,
      RecordHash   NVARCHAR(128) NULL
    );
END",
                @"
IF OBJECT_ID(N'dbo.ThreadMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ThreadMessages (
      MessageID    INT IDENTITY(1,1) PRIMARY KEY,
      ThreadID     INT NOT NULL REFERENCES dbo.MessageThreads(ThreadID),
      SenderUserID INT NOT NULL REFERENCES dbo.Users(UserID),
      Body         NVARCHAR(MAX) NOT NULL,
      ReplyToMessageID INT NULL REFERENCES dbo.ThreadMessages(MessageID),
      SentAt       DATETIME NOT NULL DEFAULT GETDATE(),
      PreviousHash NVARCHAR(128) NULL,
      RecordHash   NVARCHAR(128) NULL
    );
END",
                @"
IF OBJECT_ID(N'dbo.ThreadMessageRecipients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ThreadMessageRecipients (
      RecipientID  INT IDENTITY(1,1) PRIMARY KEY,
      MessageID    INT NOT NULL REFERENCES dbo.ThreadMessages(MessageID),
      UserID       INT NOT NULL REFERENCES dbo.Users(UserID),
      IsRead       BIT NOT NULL DEFAULT 0,
      ReadAt       DATETIME NULL,
      CONSTRAINT UQ_ThreadMessageRecipients_Message_User UNIQUE (MessageID, UserID)
    );
END"
                ,
                @"
IF OBJECT_ID(N'dbo.ClientFavorites', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientFavorites (
      FavoriteID   INT IDENTITY(1,1) PRIMARY KEY,
      UserID       INT NOT NULL REFERENCES dbo.Users(UserID),
      ClientID     INT NOT NULL REFERENCES dbo.Clients(ClientID),
      CreatedAt    DATETIME NOT NULL DEFAULT GETDATE(),
      CONSTRAINT UQ_ClientFavorites_User_Client UNIQUE (UserID, ClientID)
    );
END"
            };

            foreach (var ddl in ddlStatements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = ddl;
                await command.ExecuteNonQueryAsync();
            }

            await EnsureCompatibilityColumnsAsync(connection);
            await EnsureIndexAsync(connection, "Users", "IX_Users_Email", true, "[Email]");
            await EnsureIndexAsync(connection, "Clients", "IX_Clients_MaconomyNumber", true, "[MaconomyNumber]");
            await EnsureIndexAsync(connection, "ValidationRuns", "IX_ValidationRuns_Client_Rule_Current", false, "[ClientID], [RuleNumber], [IsCurrent]");
            await EnsureIndexAsync(connection, "ReviewSignoffs", "IX_ReviewSignoffs_Run_Reviewer", true, "[RunID], [ReviewerID]");
            await EnsureIndexAsync(connection, "AuditLog", "IX_AuditLog_Timestamp", false, "[Timestamp]");
            await EnsureIndexAsync(connection, "AuditLog", "IX_AuditLog_UserID", false, "[UserID]");
            await EnsureIndexAsync(connection, "MessageThreads", "IX_MessageThreads_Client_LastMessage", false, "[ClientID], [LastMessageAt]");
            await EnsureIndexAsync(connection, "ThreadMessages", "IX_ThreadMessages_Thread_SentAt", false, "[ThreadID], [SentAt]");
            await EnsureIndexAsync(connection, "ThreadMessageRecipients", "IX_ThreadMessageRecipients_User_Read", false, "[UserID], [IsRead]");
            await EnsureIndexAsync(connection, "ClientFavorites", "IX_ClientFavorites_User", false, "[UserID]");
        }

        private static async Task EnsureCompatibilityColumnsAsync(SqlConnection connection)
        {
            // Existing LocalDB databases may have been created from an older schema.
            // These additive upgrades keep startup idempotent and aligned with the queries in SystemDatabaseService.
            await EnsureColumnAsync(connection, "Clients", "DirectorName", "NVARCHAR(200) NOT NULL CONSTRAINT DF_Clients_DirectorName DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "DirectorEmail", "NVARCHAR(255) NOT NULL CONSTRAINT DF_Clients_DirectorEmail DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "DirectorEmpCode", "NVARCHAR(50) NOT NULL CONSTRAINT DF_Clients_DirectorEmpCode DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "Industry", "NVARCHAR(100) NULL");
            await EnsureColumnAsync(connection, "Clients", "ManagerName", "NVARCHAR(200) NOT NULL CONSTRAINT DF_Clients_ManagerName DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "ManagerEmail", "NVARCHAR(255) NOT NULL CONSTRAINT DF_Clients_ManagerEmail DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "ManagerEmpCode", "NVARCHAR(50) NOT NULL CONSTRAINT DF_Clients_ManagerEmpCode DEFAULT ''");
            await EnsureColumnAsync(connection, "Clients", "Status", "NVARCHAR(50) NOT NULL CONSTRAINT DF_Clients_Status DEFAULT 'Pending'");
            await EnsureColumnAsync(connection, "Clients", "CreatedBy", "INT NULL");
            await EnsureColumnAsync(connection, "Clients", "ApprovedBy", "INT NULL");
            await EnsureColumnAsync(connection, "Clients", "ApprovedAt", "DATETIME NULL");
            await EnsureColumnAsync(connection, "Clients", "ArchivedBy", "INT NULL");
            await EnsureColumnAsync(connection, "Clients", "ArchivedAt", "DATETIME NULL");
            await EnsureColumnAsync(connection, "Clients", "CreatedAt", "DATETIME NOT NULL CONSTRAINT DF_Clients_CreatedAt DEFAULT GETDATE()");

            await EnsureColumnAsync(connection, "ValidationRuns", "RuleNumber", "INT NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "RuleName", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "HemisServer", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "Status", "NVARCHAR(50) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "RunTimestamp", "DATETIME NOT NULL CONSTRAINT DF_ValidationRuns_RunTimestamp DEFAULT GETDATE()");
            await EnsureColumnAsync(connection, "ValidationRuns", "StudTable", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "DeceasedTable", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "StudColumn", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "DeceasedColumn", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "TotalRecords", "INT NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "PassCount", "INT NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "FailCount", "INT NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "ExceptionRate", "DECIMAL(10,4) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "ExceptionsJSON", "NVARCHAR(MAX) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "ResultsJSON", "NVARCHAR(MAX) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "RunByUserName", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "LastEditedByUserName", "NVARCHAR(255) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "LastEditedAt", "DATETIME NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "PreviousHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "RecordHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "ValidationRuns", "IsCurrent", "BIT NOT NULL CONSTRAINT DF_ValidationRuns_IsCurrent DEFAULT 1");

            await EnsureColumnAsync(connection, "UserClientAssignments", "EngagementRole", "NVARCHAR(50) NOT NULL CONSTRAINT DF_UserClientAssignments_EngagementRole DEFAULT 'DataAnalyst'");
            await EnsureColumnAsync(connection, "ReviewSignoffs", "SignoffRole", "NVARCHAR(50) NOT NULL CONSTRAINT DF_ReviewSignoffs_SignoffRole DEFAULT 'DataAnalyst'");
            await EnsureColumnAsync(connection, "AuditLog", "PreviousHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "AuditLog", "RecordHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "MessageThreads", "PreviousHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "MessageThreads", "RecordHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "ThreadMessages", "PreviousHash", "NVARCHAR(128) NULL");
            await EnsureColumnAsync(connection, "ThreadMessages", "RecordHash", "NVARCHAR(128) NULL");
            await EnsureClientStatusConstraintAsync(connection);
        }

        private static async Task EnsureIndexAsync(SqlConnection connection, string table, string indexName, bool unique, string columns)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'{EscapeSqlLiteral(indexName)}'
      AND object_id = OBJECT_ID(N'dbo.{table}')
)
BEGIN
    CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX [{indexName}] ON dbo.[{table}] ({columns});
END";
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureColumnAsync(SqlConnection connection, string table, string column, string definition)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF COL_LENGTH(N'dbo.{EscapeSqlLiteral(table)}', N'{EscapeSqlLiteral(column)}') IS NULL
BEGIN
    ALTER TABLE dbo.[{table}] ADD [{column}] {definition};
END";
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureClientStatusConstraintAsync(SqlConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
IF OBJECT_ID(N'dbo.Clients', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.Clients')
          AND name = N'CK_Clients_Status'
    )
    BEGIN
        DECLARE @existingConstraint SYSNAME;
        SELECT TOP 1 @existingConstraint = cc.name
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id = OBJECT_ID(N'dbo.Clients')
          AND cc.definition LIKE '%Status%';

        IF @existingConstraint IS NOT NULL
        BEGIN
            EXEC(N'ALTER TABLE dbo.Clients DROP CONSTRAINT [' + @existingConstraint + N'];');
        END

        ALTER TABLE dbo.Clients WITH NOCHECK
        ADD CONSTRAINT CK_Clients_Status
        CHECK (Status IN ('Pending','Approved','Active','Archived','Rejected'));
    END
END";
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SeedAdminAsync(SqlConnection connection)
        {
            const string email = "Mamishi.Madire@sng.gt.com";
            const string password = "Admin@123!";
            var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM dbo.Users WHERE Email = @Email;";
            check.Parameters.AddWithValue("@Email", email);
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;
            if (exists)
                return;

            await using var insert = connection.CreateCommand();
            insert.CommandText = @"
INSERT INTO dbo.Users
(FirstName, LastName, Email, EmployeeCode, PasswordHash, SystemRole, IsActive, MustResetPassword, PasswordSetDate, PasswordHistory, CreatedAt, CreatedBy)
VALUES
(@FirstName, @LastName, @Email, @EmployeeCode, @PasswordHash, 'Admin', 1, 0, GETDATE(), @PasswordHistory, GETDATE(), NULL);";
            insert.Parameters.AddWithValue("@FirstName", "Mamishi");
            insert.Parameters.AddWithValue("@LastName", "Madire");
            insert.Parameters.AddWithValue("@Email", email);
            insert.Parameters.AddWithValue("@EmployeeCode", "MADM007");
            insert.Parameters.AddWithValue("@PasswordHash", hash);
            insert.Parameters.AddWithValue("@PasswordHistory", JsonConvert.SerializeObject(new[] { hash }));
            await insert.ExecuteNonQueryAsync();
        }

        private static string BuildConnectionString(string server, string database, bool trustServerCertificate)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = trustServerCertificate,
                Encrypt = false,
                ConnectTimeout = 30
            };

            return builder.ConnectionString;
        }

        private static string EscapeSqlLiteral(string value) =>
            value.Replace("'", "''");
    }
}
