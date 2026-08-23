using ServerPickerX.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ServerPickerX.Services.Presets
{
    // Encodes one preset as a short copyable code. The payload is a unit separated
    // record rather than JSON so nothing here depends on a serializer that trimming
    // could strip, and so the code stays short enough to paste into a chat message.
    public static class PresetShareCode
    {
        private const string Prefix = "SPX1-";

        private const char FieldSeparator = '\u001F';

        private const int MaxDecodedLength = 64 * 1024;

        public static string Encode(PresetModel preset)
        {
            List<string> fields =
            [
                preset.Name,
                preset.GameMode,
                preset.IsClustered ? "1" : "0",
            ];

            fields.AddRange(preset.BlockedServerKeys);

            byte[] raw = Encoding.UTF8.GetBytes(string.Join(FieldSeparator, fields));

            using MemoryStream output = new();

            using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            return Prefix + ToBase64Url(output.ToArray());
        }

        public static bool TryDecode(string? code, out PresetModel preset)
        {
            preset = new PresetModel();

            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            string trimmed = code.Trim();

            if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                byte[] compressed = FromBase64Url(trimmed[Prefix.Length..]);

                using MemoryStream input = new(compressed);
                using DeflateStream inflate = new(input, CompressionMode.Decompress);
                using MemoryStream output = new();

                // Bounded copy, a hand edited code should not be able to inflate
                // into an allocation large enough to matter
                byte[] buffer = new byte[8192];
                int read;

                while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxDecodedLength)
                    {
                        return false;
                    }

                    output.Write(buffer, 0, read);
                }

                string[] fields = Encoding.UTF8
                    .GetString(output.ToArray())
                    .Split(FieldSeparator);

                if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[1]))
                {
                    return false;
                }

                preset = new PresetModel
                {
                    Name = fields[0],
                    GameMode = fields[1],
                    IsClustered = fields[2] == "1",
                    BlockedServerKeys = fields
                        .Skip(3)
                        .Where(serverKey => !string.IsNullOrWhiteSpace(serverKey))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string padded = value
                .Replace('-', '+')
                .Replace('_', '/');

            return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
        }
    }
}
