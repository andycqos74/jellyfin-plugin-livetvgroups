using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LiveTvGroups.Configuration;

/// <summary>
/// Settings for the Live TV Groups plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// How long a parsed playlist is reused before it is fetched again. Playlists
    /// change rarely and can be large, so the default is deliberately generous.
    /// Set to 0 to disable caching.
    /// </summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>
    /// Seconds to wait for a playlist download before giving up.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Sort groups by name instead of keeping the order they appear in the playlist.
    /// Only applies to groups with no manual position in <see cref="Groups"/>.
    /// </summary>
    public bool SortGroupsAlphabetically { get; set; }

    /// <summary>
    /// Name given to channels whose playlist entry carries no <c>group-title</c>.
    /// </summary>
    public string UngroupedName { get; set; } = "Ungrouped";

    /// <summary>
    /// Manual order and visibility, most significant first.
    /// </summary>
    /// <remarks>
    /// Groups listed here come first, in this order. Anything the playlist gains
    /// later is appended afterwards rather than disappearing, so a new group from
    /// the provider still shows up without a visit to this page.
    /// </remarks>
    public GroupSetting[] Groups { get; set; } = Array.Empty<GroupSetting>();
}

/// <summary>
/// One group's manual position and visibility.
/// </summary>
public class GroupSetting
{
    public string Name { get; set; } = string.Empty;

    public bool Hidden { get; set; }
}
