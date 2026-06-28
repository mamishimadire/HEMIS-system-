using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using HemisAudit.Models;

namespace HemisAudit.Services
{
    public interface IPasswordPolicyService
    {
        int MaxPasswordAgeDays { get; }
        int WarningWindowDays { get; }
        IReadOnlyList<string> ValidatePassword(ApplicationUser user, string password, string? currentPasswordHash = null);
        string BuildPasswordHistory(string? existingHistoryJson, string newHash);
        DateTime? GetPasswordReferenceDate(ApplicationUser user);
        int? GetPasswordWarningDays(ApplicationUser user, DateTime utcNow);
        int GetPasswordDaysRemaining(ApplicationUser user, DateTime utcNow);
        bool IsPasswordExpired(ApplicationUser user, DateTime utcNow);
        int GetPasswordAgeDays(ApplicationUser user, DateTime utcNow);
    }

    public class PasswordPolicyService : IPasswordPolicyService
    {
        public const int DefaultMaxPasswordAgeDays = 30;
        public const int DefaultWarningWindowDays = 7;

        private static readonly Regex SpecialCharRegex = new(@"[^A-Za-z0-9]", RegexOptions.Compiled);
        private readonly IPasswordHasher<ApplicationUser> _hasher;

        public int MaxPasswordAgeDays => DefaultMaxPasswordAgeDays;
        public int WarningWindowDays => DefaultWarningWindowDays;

        public PasswordPolicyService(IPasswordHasher<ApplicationUser> hasher)
        {
            _hasher = hasher;
        }

        public IReadOnlyList<string> ValidatePassword(ApplicationUser user, string password, string? currentPasswordHash = null)
        {
            var errors = new List<string>();
            var normalizedPassword = password ?? string.Empty;

            if (normalizedPassword.Length < 8)
                errors.Add("Password must be at least 8 characters long.");

            if (!normalizedPassword.Any(char.IsUpper))
                errors.Add("Password must contain at least one uppercase letter.");

            if (!normalizedPassword.Any(char.IsLower))
                errors.Add("Password must contain at least one lowercase letter.");

            if (!normalizedPassword.Any(char.IsDigit))
                errors.Add("Password must contain at least one digit.");

            if (!SpecialCharRegex.IsMatch(normalizedPassword))
                errors.Add("Password must contain at least one special character.");

            var loweredPassword = normalizedPassword.ToLowerInvariant();
            var firstName = (user.FirstName ?? string.Empty).Trim().ToLowerInvariant();
            var lastName = (user.LastName ?? string.Empty).Trim().ToLowerInvariant();
            var emailPrefix = GetEmailPrefix(user.Email).ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(firstName) && loweredPassword.Contains(firstName))
                errors.Add("Password must not contain your first name.");

            if (!string.IsNullOrWhiteSpace(lastName) && loweredPassword.Contains(lastName))
                errors.Add("Password must not contain your last name.");

            if (!string.IsNullOrWhiteSpace(emailPrefix) && loweredPassword.Contains(emailPrefix))
                errors.Add("Password must not contain your email prefix.");

            var activeHash = currentPasswordHash ?? user.PasswordHash;
            if (!string.IsNullOrWhiteSpace(activeHash) &&
                _hasher.VerifyHashedPassword(user, activeHash, normalizedPassword) == PasswordVerificationResult.Success)
            {
                errors.Add("Password must not match the current active password.");
            }

            foreach (var previousHash in ReadPasswordHistory(user.PasswordHistory))
            {
                if (_hasher.VerifyHashedPassword(user, previousHash, normalizedPassword) == PasswordVerificationResult.Success)
                {
                    errors.Add("Password must not match any of your last 5 passwords.");
                    break;
                }
            }

            return errors;
        }

        public string BuildPasswordHistory(string? existingHistoryJson, string newHash)
        {
            var history = ReadPasswordHistory(existingHistoryJson).ToList();
            history.Insert(0, newHash);
            return JsonSerializer.Serialize(history.Take(5).ToList());
        }

        public DateTime? GetPasswordReferenceDate(ApplicationUser user)
        {
            var referenceDate = user.PasswordSetDate ?? user.CreatedAt;
            return referenceDate == default ? null : referenceDate;
        }

        public int? GetPasswordWarningDays(ApplicationUser user, DateTime utcNow)
        {
            var daysRemaining = GetPasswordDaysRemaining(user, utcNow);
            return daysRemaining > 0 && daysRemaining <= WarningWindowDays
                ? daysRemaining
                : null;
        }

        public int GetPasswordDaysRemaining(ApplicationUser user, DateTime utcNow)
        {
            return Math.Max(0, MaxPasswordAgeDays - GetPasswordAgeDays(user, utcNow));
        }

        public bool IsPasswordExpired(ApplicationUser user, DateTime utcNow)
        {
            return GetPasswordAgeDays(user, utcNow) >= MaxPasswordAgeDays;
        }

        public int GetPasswordAgeDays(ApplicationUser user, DateTime utcNow)
        {
            var passwordSetDate = GetPasswordReferenceDate(user);
            // No reference date means the account pre-dates the policy — treat as already expired.
            return passwordSetDate.HasValue ? Math.Max(0, (utcNow - passwordSetDate.Value).Days) : MaxPasswordAgeDays;
        }

        private static IEnumerable<string> ReadPasswordHistory(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<string>();

            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(json);
                return values?.Where(v => !string.IsNullOrWhiteSpace(v)) ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string GetEmailPrefix(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;

            var at = email.IndexOf('@');
            return at > 0 ? email[..at] : email;
        }
    }
}
