using System.Globalization;
using System.Text;

namespace VictumPOS.PrintBridge.Service.Services
{
    internal static class EscPosTextNormalizer
    {
        public static string Normalize(string content)
        {
            if (string.IsNullOrEmpty(content))
                return "";

            var source = ReplaceKnownCharacters(content).Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(source.Length);

            foreach (var character in source)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsControl(character))
                {
                    builder.Append(character);
                    continue;
                }

                if (character >= 32 && character <= 126)
                    builder.Append(character);
                else if (!char.IsControl(character))
                    builder.Append('?');
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string ReplaceKnownCharacters(string value)
        {
            return value
                .Replace('\u00A0', ' ')
                .Replace('\u00BF', '?')
                .Replace('\u00A1', '!')
                .Replace('\u2018', '\'')
                .Replace('\u2019', '\'')
                .Replace('\u201C', '"')
                .Replace('\u201D', '"')
                .Replace('\u2013', '-')
                .Replace("\u20AC", "EUR")
                .Replace('\u2014', '-')
                .Replace('\u2026', '.')
                .Replace('\u2022', '*')
                .Replace('\u00B0', 'o')
                .Replace('\u00BA', 'o')
                .Replace('\u00AA', 'a')
                .Replace('\u00D7', 'x');
        }

    }
}
