using System;
using System.Globalization;
using System.Text;

namespace TaskImporter.Core.Clipboard;

/// <summary>
/// Minimal HTML entity decoding.
///
/// Hand-rolled rather than using System.Net.WebUtility so that Core stays free
/// of surprises about which entities a given runtime knows, and so the
/// behaviour is identical under test and in the extension. Page titles and
/// anchor text only ever need the common handful plus numeric references.
/// </summary>
public static class HtmlText
{
    public static string Decode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value!.IndexOf('&') < 0) return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            var amp = value.IndexOf('&', index);
            if (amp < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, amp - index);

            var semicolon = value.IndexOf(';', amp + 1);
            if (semicolon < 0 || semicolon - amp > 12)
            {
                builder.Append('&');
                index = amp + 1;
                continue;
            }

            var entity = value[(amp + 1)..semicolon];
            var decoded = DecodeEntity(entity);

            if (decoded is null)
            {
                builder.Append('&');
                index = amp + 1;
                continue;
            }

            builder.Append(decoded);
            index = semicolon + 1;
        }

        return builder.ToString();
    }

    private static string? DecodeEntity(string entity)
    {
        if (entity.Length == 0) return null;

        if (entity[0] == '#')
        {
            var isHex = entity.Length > 1 && (entity[1] is 'x' or 'X');
            var digits = isHex ? entity[2..] : entity[1..];
            if (digits.Length == 0) return null;

            var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out var code)) return null;
            if (code is < 0 or > 0x10FFFF) return null;

            try
            {
                return char.ConvertFromUtf32(code);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return entity.ToLowerInvariant() switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            "ndash" => "–",
            "mdash" => "—",
            "hellip" => "…",
            "lsquo" => "‘",
            "rsquo" => "’",
            "ldquo" => "“",
            "rdquo" => "”",
            "middot" => "·",
            "bull" => "•",
            "trade" => "™",
            "copy" => "©",
            "reg" => "®",
            _ => null,
        };
    }
}
