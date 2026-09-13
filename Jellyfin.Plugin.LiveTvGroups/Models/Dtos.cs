using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTvGroups.Models;

/// <summary>
/// One <c>group-title</c> from the playlist, with the Jellyfin channels that belong to it.
/// </summary>
public class ChannelGroupDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Library item ids, in playlist order. These are the same ids
    /// <c>/LiveTv/Channels</c> returns, so a client can use them directly.
    /// </summary>
    public IReadOnlyList<Guid> ChannelIds { get; set; } = Array.Empty<Guid>();
}

/// <summary>
/// The grouping, plus enough counters to tell "no playlist configured" apart from
/// "playlist configured but nothing matched" without reading the server log.
/// </summary>
public class ChannelGroupsResultDto
{
    public IReadOnlyList<ChannelGroupDto> Groups { get; set; } = Array.Empty<ChannelGroupDto>();

    /// <summary>Live TV channels Jellyfin currently has.</summary>
    public int TotalChannels { get; set; }

    /// <summary>Channels placed into a group from a playlist entry.</summary>
    public int MatchedChannels { get; set; }

    /// <summary>Playlist entries read across every M3U tuner.</summary>
    public int PlaylistEntries { get; set; }

    /// <summary>M3U tuner hosts found in the Live TV configuration.</summary>
    public int PlaylistCount { get; set; }

    /// <summary>Per-playlist failures. A failure never empties the response.</summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    public DateTime GeneratedAt { get; set; }
}
