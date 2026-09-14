using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LiveTvGroups.Configuration;
using Jellyfin.Plugin.LiveTvGroups.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Builds the channel grouping: read every M3U tuner's playlist, parse the
/// <c>group-title</c> values, and match each entry to the Live TV channel Jellyfin
/// created from it.
/// </summary>
public class ChannelGroupService
{
    private readonly IServerConfigurationManager _config;
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChannelGroupService> _logger;

    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private ChannelGroupsResultDto? _cached;
    private DateTime _cachedAt = DateTime.MinValue;

    public ChannelGroupService(
        IServerConfigurationManager config,
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILogger<ChannelGroupService> logger)
    {
        _config = config;
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static PluginConfiguration Config =>
        LiveTvGroupsPlugin.Instance?.Configuration ?? new PluginConfiguration();

    public void ClearCache()
    {
        _cached = null;
        _cachedAt = DateTime.MinValue;
    }

    /// <summary>
    /// The grouping as configured: manual order first, hidden groups dropped unless
    /// <paramref name="includeHidden"/> is set (the settings page needs to list them).
    /// </summary>
    public async Task<ChannelGroupsResultDto> GetGroupsAsync(
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        var raw = await GetRawAsync(cancellationToken).ConfigureAwait(false);
        return Project(raw, includeHidden);
    }

    /// <summary>
    /// Ordering and visibility are applied on the way out rather than baked into the
    /// cache, so saving the settings page takes effect on the next request instead of
    /// after the playlist cache expires.
    /// </summary>
    private static ChannelGroupsResultDto Project(ChannelGroupsResultDto raw, bool includeHidden)
    {
        var config = Config;
        var settings = config.Groups ?? Array.Empty<GroupSetting>();

        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < settings.Length; i++)
        {
            var name = settings[i].Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            position[name] = i;
            if (settings[i].Hidden)
            {
                hidden.Add(name);
            }
        }

        var visible = raw.Groups
            .Where(g => includeHidden || !hidden.Contains(g.Name))
            .Select(g => new ChannelGroupDto
            {
                Name = g.Name,
                ChannelIds = g.ChannelIds,
                Hidden = hidden.Contains(g.Name)
            })
            .ToList();

        var manual = visible
            .Where(g => position.ContainsKey(g.Name))
            .OrderBy(g => position[g.Name]);

        // Groups the playlist has gained since the settings were saved keep their
        // playlist order and follow the manual ones, rather than vanishing.
        IEnumerable<ChannelGroupDto> rest = visible.Where(g => !position.ContainsKey(g.Name));
        if (config.SortGroupsAlphabetically)
        {
            rest = rest.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        return new ChannelGroupsResultDto
        {
            Groups = manual.Concat(rest).ToList(),
            TotalChannels = raw.TotalChannels,
            MatchedChannels = raw.MatchedChannels,
            PlaylistEntries = raw.PlaylistEntries,
            PlaylistCount = raw.PlaylistCount,
            Errors = raw.Errors,
            GeneratedAt = raw.GeneratedAt
        };
    }

    private async Task<ChannelGroupsResultDto> GetRawAsync(CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(0, Config.CacheMinutes));

        if (_cached is not null && ttl > TimeSpan.Zero && DateTime.UtcNow - _cachedAt < ttl)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && ttl > TimeSpan.Zero && DateTime.UtcNow - _cachedAt < ttl)
            {
                return _cached;
            }

            var result = await BuildAsync(cancellationToken).ConfigureAwait(false);
            _cached = result;
            _cachedAt = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ChannelGroupsResultDto> BuildAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var channels = GetChannels();

        var byName = BuildLookup(channels, c => M3uPlaylistParser.NormaliseName(c.Name));
        var byNumber = BuildLookup(channels, c => (c.Number ?? string.Empty).Trim());

        // Group name -> channel ids, insertion-ordered so playlist order survives.
        var groups = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        var groupOrder = new List<string>();
        var claimed = new HashSet<Guid>();

        var tunerHosts = GetM3uTunerHosts();
        var entryCount = 0;

        foreach (var tuner in tunerHosts)
        {
            IReadOnlyList<M3uEntry> entries;

            try
            {
                var content = await LoadPlaylistAsync(tuner, cancellationToken).ConfigureAwait(false);
                entries = M3uPlaylistParser.Parse(content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var label = string.IsNullOrWhiteSpace(tuner.FriendlyName) ? tuner.Url : tuner.FriendlyName;
                _logger.LogError(ex, "Unable to read M3U playlist for tuner {Tuner}", label);
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}",
                    label,
                    ex.Message));
                continue;
            }

            entryCount += entries.Count;

            foreach (var entry in entries)
            {
                var channel = Resolve(entry, byName, byNumber);
                if (channel is null || !claimed.Add(channel.Id))
                {
                    continue;
                }

                var groupName = string.IsNullOrWhiteSpace(entry.Group)
                    ? Config.UngroupedName
                    : entry.Group!;

                if (!groups.TryGetValue(groupName, out var list))
                {
                    list = new List<Guid>();
                    groups[groupName] = list;
                    groupOrder.Add(groupName);
                }

                list.Add(channel.Id);
            }
        }

        // Anything the playlists did not account for still has to be reachable in a
        // client that browses by group, so it goes to the end rather than nowhere.
        var leftovers = channels.Where(c => !claimed.Contains(c.Id)).ToList();
        if (leftovers.Count > 0 && groupOrder.Count > 0)
        {
            var name = Config.UngroupedName;
            if (!groups.TryGetValue(name, out var list))
            {
                list = new List<Guid>();
                groups[name] = list;
                groupOrder.Add(name);
            }

            list.AddRange(leftovers.Select(c => c.Id));
        }

        return new ChannelGroupsResultDto
        {
            Groups = groupOrder
                .Select(name => new ChannelGroupDto { Name = name, ChannelIds = groups[name] })
                .ToList(),
            TotalChannels = channels.Count,
            MatchedChannels = claimed.Count,
            PlaylistEntries = entryCount,
            PlaylistCount = tunerHosts.Count,
            Errors = errors,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private List<LiveTvChannel> GetChannels() =>
        _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.LiveTvChannel },
                IsVirtualItem = false,
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            })
            .OfType<LiveTvChannel>()
            .ToList();

    private List<TunerHostInfo> GetM3uTunerHosts()
    {
        var options = _config.GetConfiguration<LiveTvOptions>("livetv");

        return (options?.TunerHosts ?? Array.Empty<TunerHostInfo>())
            .Where(t => string.Equals(t.Type, "m3u", StringComparison.OrdinalIgnoreCase))
            .Where(t => !string.IsNullOrWhiteSpace(t.Url))
            .ToList();
    }

    private async Task<string> LoadPlaylistAsync(TunerHostInfo tuner, CancellationToken cancellationToken)
    {
        var url = tuner.Url;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await File.ReadAllTextAsync(url, cancellationToken).ConfigureAwait(false);
        }

        using var client = _httpClientFactory.CreateClient(NamedClient.Default);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, Config.RequestTimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(tuner.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", tuner.UserAgent);
        }

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Name first: the plugin derives it the same way Jellyfin did when it created
    /// the channel, so it is an exact match rather than a guess. Number is the
    /// fallback for playlists whose names have since been edited server-side.
    /// Ambiguous keys are skipped rather than guessed at.
    /// </summary>
    private static LiveTvChannel? Resolve(
        M3uEntry entry,
        Dictionary<string, List<LiveTvChannel>> byName,
        Dictionary<string, List<LiveTvChannel>> byNumber)
    {
        var name = M3uPlaylistParser.NormaliseName(entry.Name);
        if (name.Length > 0 && byName.TryGetValue(name, out var named) && named.Count == 1)
        {
            return named[0];
        }

        var number = entry.Number?.Trim();
        if (!string.IsNullOrEmpty(number) && byNumber.TryGetValue(number, out var numbered) && numbered.Count == 1)
        {
            return numbered[0];
        }

        return null;
    }

    private static Dictionary<string, List<LiveTvChannel>> BuildLookup(
        IEnumerable<LiveTvChannel> channels,
        Func<LiveTvChannel, string> key)
    {
        var map = new Dictionary<string, List<LiveTvChannel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            var k = key(channel);
            if (string.IsNullOrEmpty(k))
            {
                continue;
            }

            if (!map.TryGetValue(k, out var list))
            {
                list = new List<LiveTvChannel>();
                map[k] = list;
            }

            list.Add(channel);
        }

        return map;
    }
}
