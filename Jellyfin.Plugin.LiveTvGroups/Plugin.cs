using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.LiveTvGroups.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LiveTvGroups;

/// <summary>
/// Live TV Groups plugin entry point.
/// </summary>
/// <remarks>
/// Jellyfin parses <c>group-title</c> out of an M3U playlist into
/// <c>ChannelInfo.ChannelGroup</c> and then discards it: the guide refresh only
/// copies <c>ChannelInfo.Tags</c> onto the library item, and the M3U parser never
/// sets <c>Tags</c>. There is also no GET on <c>/LiveTv/TunerHosts</c>. So the
/// grouping a playlist carries is invisible to every client. This plugin reads the
/// playlists back, parses the groups itself, matches entries to the channels
/// Jellyfin created from them, and serves the result over one endpoint.
/// </remarks>
public class LiveTvGroupsPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public LiveTvGroupsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static LiveTvGroupsPlugin? Instance { get; private set; }

    public override string Name => "Live TV Groups";

    public override string Description =>
        "Exposes the group-title categories from your M3U playlists over the API, so clients can browse Live TV channels by category.";

    public override Guid Id => Guid.Parse("5f2b9c14-8d7a-4e63-b1a0-3c6e9f04d2a8");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace)
        };
    }
}
