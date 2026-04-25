using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HemisAudit.Models;
using System.Text.Json;

namespace HemisAudit.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var db = services.GetRequiredService<ApplicationDbContext>();

            await db.Database.EnsureCreatedAsync();

            await EnsureValidationSchemaAsync(db);
            await EnsureIdentitySchemaAsync(db);
            await CleanDuplicateClientsAsync(db);
            await CleanDuplicateClientUsersAsync(db);
            await CreateIndexesAsync(db);

            await SeedRolesAsync(roleManager);
            await SeedDefaultAdminAsync(userManager);
        }

        private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            string[] roles = { "Admin", "Director", "Manager", "DataAnalyst", "Trainee" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        private static async Task SeedDefaultAdminAsync(UserManager<ApplicationUser> userManager)
        {
            const string adminEmail = "Mamishi.Madire@sng.gt.com";

            var existing = await userManager.FindByEmailAsync(adminEmail);
            if (existing != null)
            {
                existing.PasswordSetDate ??= DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(existing.PasswordHistory))
                {
                    var currentHash = existing.PasswordHash ?? string.Empty;
                    existing.PasswordHistory = JsonSerializer.Serialize(new[] { currentHash });
                    await userManager.UpdateAsync(existing);
                }

                if (!await userManager.IsInRoleAsync(existing, "Admin"))
                    await userManager.AddToRoleAsync(existing, "Admin");

                return;
            }

            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Mamishi",
                LastName = "Madire",
                EmployeeCode = "MADM007",
                EmailConfirmed = true,
                IsActive = true,
                PasswordSetDate = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(admin, "Admin@123!");
            if (result.Succeeded)
            {
                var hash = admin.PasswordHash ?? string.Empty;
                admin.PasswordHistory = JsonSerializer.Serialize(new[] { hash });
                await userManager.AddToRoleAsync(admin, "Admin");
                await userManager.UpdateAsync(admin);
            }
        }

        private static async Task EnsureValidationSchemaAsync(ApplicationDbContext db)
        {
            await EnsureColumnAsync(db, "ValidationRuns", "IsCurrent", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(db, "ValidationRuns", "ResultsJson", "TEXT NULL");
        }

        private static async Task EnsureIdentitySchemaAsync(ApplicationDbContext db)
        {
            await EnsureColumnAsync(db, "AspNetUsers", "PasswordSetDate", "TEXT NULL");
            await EnsureColumnAsync(db, "AspNetUsers", "PasswordHistory", "TEXT NULL");
            await EnsureColumnAsync(db, "AspNetUsers", "ProfilePicturePath", "TEXT NULL");
            await EnsureColumnAsync(db, "AspNetUsers", "Gender", "TEXT NULL");
            await EnsureColumnAsync(db, "AspNetUsers", "Department", "TEXT NULL");
            await EnsureColumnAsync(db, "AspNetUsers", "OfficeAddress", "TEXT NULL");
        }

        private static async Task CreateIndexesAsync(ApplicationDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Clients_Name_FiscalYear ON Clients (Name, FiscalYear);");

            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_ClientUsers_ClientId_UserId ON ClientUsers (ClientId, UserId);");

            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS IX_ValidationRuns_ClientRuleCurrent ON ValidationRuns (ClientId, RuleNumber, IsCurrent, RunAt DESC);");
        }

        private static async Task CleanDuplicateClientsAsync(ApplicationDbContext db)
        {
            var clients = await db.Clients
                .Include(c => c.ClientUsers)
                .Include(c => c.ValidationRuns)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .ToListAsync();

            var groups = clients
                .GroupBy(c => new { Name = NormalizeKey(c.Name), FiscalYear = NormalizeKey(c.FiscalYear) })
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var keeper = group.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).First();
                var keeperUsers = keeper.ClientUsers
                    .Select(cu => cu.UserId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var duplicate in group.Skip(1))
                {
                    foreach (var assignment in duplicate.ClientUsers.ToList())
                    {
                        if (!keeperUsers.Contains(assignment.UserId))
                        {
                            assignment.ClientId = keeper.Id;
                            keeperUsers.Add(assignment.UserId);
                        }
                        else
                        {
                            db.ClientUsers.Remove(assignment);
                        }
                    }

                    foreach (var run in duplicate.ValidationRuns.ToList())
                    {
                        run.ClientId = keeper.Id;
                    }

                    db.Clients.Remove(duplicate);
                }
            }

            await db.SaveChangesAsync();
        }

        private static async Task CleanDuplicateClientUsersAsync(ApplicationDbContext db)
        {
            var assignments = await db.ClientUsers
                .OrderBy(cu => cu.AssignedAt)
                .ThenBy(cu => cu.Id)
                .ToListAsync();

            var groups = assignments
                .GroupBy(cu => new { cu.ClientId, cu.UserId })
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var keeper = group.OrderBy(cu => cu.AssignedAt).ThenBy(cu => cu.Id).First();
                var representative = group.OrderByDescending(cu => cu.IsActive)
                    .ThenByDescending(cu => cu.AssignedAt)
                    .ThenByDescending(cu => cu.Id)
                    .First();

                keeper.EngagementRole = representative.EngagementRole;
                keeper.AssignedByUserId = representative.AssignedByUserId;
                keeper.AssignedAt = representative.AssignedAt;
                keeper.IsActive = group.Any(cu => cu.IsActive);

                foreach (var extra in group.Skip(1))
                    db.ClientUsers.Remove(extra);
            }

            await db.SaveChangesAsync();
        }

        private static async Task EnsureColumnAsync(ApplicationDbContext db, string tableName, string columnName, string definition)
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            try
            {
                var exists = false;
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"PRAGMA table_info([{tableName}]);";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var currentName = reader.GetString(1);
                        if (string.Equals(currentName, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists)
                {
                    await using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE [{tableName}] ADD COLUMN [{columnName}] {definition};";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        private static string NormalizeKey(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }
}
