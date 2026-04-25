using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HemisAudit.Helpers
{
    public static class AvatarHelper
    {
        private static readonly string[] Palette =
        {
            "#C62828",
            "#1565C0",
            "#2E7D32",
            "#6A1B9A",
            "#00838F",
            "#EF6C00",
            "#5D4037",
            "#283593"
        };

        public static string GetAvatarSource(string? userId, string? firstName, string? lastName, string? profilePicturePath)
        {
            if (!string.IsNullOrWhiteSpace(profilePicturePath))
                return profilePicturePath;

            var initials = WebUtility.HtmlEncode(GetInitials(firstName, lastName));
            var color = GetConsistentColor(userId);
            var svg = $"""
<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>
  <circle cx='50' cy='50' r='50' fill='{color}' />
  <text x='50' y='56' text-anchor='middle' font-family='Arial, Helvetica, sans-serif' font-size='36' font-weight='700' fill='#FFFFFF'>{initials}</text>
</svg>
""";
            return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
        }

        public static string GetInitials(string? firstName, string? lastName)
        {
            var first = string.IsNullOrWhiteSpace(firstName) ? "" : firstName.Trim()[0].ToString().ToUpperInvariant();
            var last = string.IsNullOrWhiteSpace(lastName) ? "" : lastName.Trim()[0].ToString().ToUpperInvariant();
            var initials = $"{first}{last}";
            return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
        }

        public static string GetConsistentColor(string? seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return Palette[0];

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
            var index = bytes[0] % Palette.Length;
            return Palette[index];
        }
    }
}
