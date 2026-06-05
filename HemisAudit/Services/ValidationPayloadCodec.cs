using System.IO.Compression;
using System.Text;

namespace HemisAudit.Services
{
    internal static class ValidationPayloadCodec
    {
        private const string Prefix = "gz:";
        private const int CompressionThresholdChars = 4096;

        public static string Encode(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            if (value.Length < CompressionThresholdChars)
                return value;

            var inputBytes = Encoding.UTF8.GetBytes(value);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(inputBytes, 0, inputBytes.Length);
            }

            var compressed = Convert.ToBase64String(output.ToArray());
            return compressed.Length + Prefix.Length < value.Length
                ? Prefix + compressed
                : value;
        }

        public static string? Decode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
                return value;

            var payload = value.Substring(Prefix.Length);
            var compressedBytes = Convert.FromBase64String(payload);
            using var input = new MemoryStream(compressedBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
