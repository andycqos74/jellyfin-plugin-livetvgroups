using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Models;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>
/// The plugin's only read endpoint: the M3U grouping, keyed to Jellyfin channel ids.
/// </summary>
[ApiController]
[Route("LiveTvGroups")]
[Produces("application/json")]
[Authorize]
public class LiveTvGroupsController : ControllerBase
{
    private readonly ChannelGroupService _service;

    public LiveTvGroupsController(ChannelGroupService service)
    {
        _service = service;
    }

    /// <summary>
    /// Channel groups from the configured M3U playlists.
    /// </summary>
    /// <remarks>
    /// Readable by any authenticated user: it exposes group names and channel ids,
    /// never the playlist URL or tuner credentials.
    /// </remarks>
    /// <param name="includeHidden">
    /// Include groups hidden in the settings, flagged as such. The settings page asks
    /// for these so they can be reordered and turned back on; clients should not.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("Groups")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelGroupsResultDto>> GetGroups(
        [FromQuery] bool includeHidden,
        CancellationToken cancellationToken)
        => await _service.GetGroupsAsync(includeHidden, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Drop the cached playlist so the next request re-reads it.
    /// </summary>
    [HttpPost("Refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Refresh()
    {
        _service.ClearCache();
        return NoContent();
    }
}
