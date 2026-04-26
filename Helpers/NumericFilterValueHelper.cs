using System.Text;

namespace HemisAudit.Helpers
{
    public static class NumericFilterValueHelper
    {
        private static readonly char[] DefaultSeparators = [',', ';', '\r', '\n'];

        public static List<string> ParseValues(string? source, IEnumerable<string>? fallbackValues = null)
        {
            var values = (source ?? string.Empty)
                .Split(DefaultSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.Trim().Trim('"', '\''))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count > 0)
                return values;

            if (fallbackValues == null)
                return values;

            return fallbackValues
                .Select(value => (value ?? string.Empty).Trim().Trim('"', '\''))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string NormalizeNumericLikeValue(string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
                return string.Empty;

            if (!trimmed.All(char.IsDigit))
                return trimmed;

            var normalized = trimmed.TrimStart('0');
            return string.IsNullOrEmpty(normalized) ? "0" : normalized;
        }

        public static string BuildNormalizedSqlExpression(string trimmedExpression) =>
            $@"CASE
    WHEN {trimmedExpression} = '' THEN ''
    WHEN {trimmedExpression} LIKE '%[^0-9]%' THEN {trimmedExpression}
    WHEN PATINDEX('%[^0]%', {trimmedExpression}) = 0 THEN '0'
    ELSE SUBSTRING({trimmedExpression}, PATINDEX('%[^0]%', {trimmedExpression}), LEN({trimmedExpression}))
END";

        public static string DescribeSmartMatching(IEnumerable<string> rawValues)
        {
            var values = rawValues.ToList();
            if (values.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            builder.Append(string.Join(", ", values));

            var normalizedValues = values
                .Select(NormalizeNumericLikeValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!values.SequenceEqual(normalizedValues, StringComparer.OrdinalIgnoreCase))
                builder.Append($" (normalized: {string.Join(", ", normalizedValues)})");

            return builder.ToString();
        }
    }
}
