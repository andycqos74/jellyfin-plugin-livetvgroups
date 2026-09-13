using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// One <c>#EXTINF</c> line from an M3U playlist.
/// </summary>
public sealed class M3uEntry
{
    public string? Name { get; init; }

    public string? Group { get; init; }

    public string? Number { get; init; }

    public string? TvgId { get; init; }
}

/// <summary>
/// Reads channel name, number and <c>group-title</c> out of an M3U playlist.
/// </summary>
/// <remarks>
/// Name derivation deliberately mirrors Jellyfin's own <c>M3uParser.GetChannelName</c>:
/// the text after the last comma (with a leading "84." style number stripped), then
/// <c>tvg-name</c>, then <c>tvg-id</c>. Matching entries back to channels relies on
/// producing the same string Jellyfin stored as the channel name, so any divergence
/// here shows up as unmatched channels.
/// </remarks>
public static partial class M3uPlaylistParser
{
    [GeneratedRegex(@"([a-z0-9_-]+)=\""([^\""]*)\""", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    public static IReadOnlyList<M3uEntry> Parse(string content)
    {
        var entries = new List<M3uEntry>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return entries;
        }

        var lines = content.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var remaining = line;

            foreach (Match match in AttributeRegex().Matches(line))
            {
                attributes[match.Groups[1].Value] = match.Groups[2].Value;
                remaining = remaining.Replace(match.Value, string.Empty, StringComparison.Ordinal);
            }

            attributes.TryGetValue("group-title", out var group);

            entries.Add(new M3uEntry
            {
                Name = GetName(remaining, attributes),
                Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim(),
                Number = GetNumber(attributes),
                TvgId = attributes.GetValueOrDefault("tvg-id")
            });
        }

        return entries;
    }

    /// <summary>
    /// Normalises a name for comparison: case, surrounding space and repeated
    /// internal whitespace all vary between a playlist and what ends up on the item.
    /// </summary>
    public static string NormaliseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(name.Trim(), " ").ToUpperInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static string? GetName(string extInf, Dictionary<string, string> attributes)
    {
        var nameParts = extInf.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var name = nameParts.Length > 1 ? nameParts[^1].Trim() : string.Empty;

        // "84. VOX Schweiz" and "84.0 - VOX Schweiz" both mean channel 84, VOX Schweiz.
        if (!string.IsNullOrWhiteSpace(name))
        {
            var numberIndex = name.IndexOf(' ', StringComparison.Ordinal);
            if (numberIndex > 0)
            {
                var numberPart = name.AsSpan(0, numberIndex).Trim(" .".AsSpan());
                if (double.TryParse(numberPart, CultureInfo.InvariantCulture, out _))
                {
                    name = name.AsSpan(numberIndex + 1).Trim(" -".AsSpan()).ToString();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            attributes.TryGetValue("tvg-name", out name!);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            attributes.TryGetValue("tvg-id", out name!);
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? GetNumber(Dictionary<string, string> attributes)
    {
        foreach (var key in new[] { "tvg-chno", "tvg-id", "channel-id" })
        {
            if (attributes.TryGetValue(key, out var value)
                && double.TryParse(value, CultureInfo.InvariantCulture, out _))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
