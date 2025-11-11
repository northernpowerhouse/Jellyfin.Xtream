// Copyright (C) 2022  Kevin Jilissen

using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Api.Models;
using Jellyfin.Xtream.Client;
using Jellyfin.Xtream.Client.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Api;

/// <summary>
/// The Jellyfin Xtream configuration API.
/// </summary>
[ApiController]
[Route("[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class XtreamController : ControllerBase
{
    private readonly IXtreamClient _xtreamClient;
    private readonly ILogger<XtreamController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamController"/> class.
    /// </summary>
    /// <param name="xtreamClient">The Xtream client.</param>
    /// <param name="logger">The logger.</param>
    public XtreamController(IXtreamClient xtreamClient, ILogger<XtreamController> logger)
    {
        _xtreamClient = xtreamClient;
        _logger = logger;
    }

    /// <summary>
    /// Log a configuration change.
    /// </summary>
    /// <param name="request">The log request.</param>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    /// <response code="204">Configuration change logged successfully.</response>
    [HttpPost("LogConfigChange")]
    [ProducesResponseType(204)]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult LogConfigChange([FromBody] LogConfigChangeRequest request)
    {
        // Configuration change notification received (no verbose logging)

        return NoContent();
    }

    private static CategoryResponse CreateCategoryResponse(Category category) =>
        new()
        {
            Id = category.CategoryId,
            Name = category.CategoryName,
        };

    /// <summary>
    /// Test the configured provider and return account/server details.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>A <see cref="ProviderTestResponse"/> containing provider status and server info.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("TestProvider")]
    public async Task<ActionResult<ProviderTestResponse>> TestProvider(CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        PlayerApi info = await _xtreamClient.GetUserAndServerInfoAsync(plugin.Creds, cancellationToken).ConfigureAwait(false);
        return Ok(new ProviderTestResponse()
        {
            ActiveConnections = info.UserInfo.ActiveCons,
            ExpiryDate = info.UserInfo.ExpDate,
            MaxConnections = info.UserInfo.MaxConnections,
            ServerTime = info.ServerInfo.TimeNow,
            ServerTimezone = info.ServerInfo.Timezone,
            Status = info.UserInfo.Status,
            SupportsMpegTs = info.UserInfo.AllowedOutputFormats.Contains("ts"),
        });
    }

    /// <summary>
    /// Get all Live TV categories from the Xtream provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="CategoryResponse"/>.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("LiveCategories")]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> GetLiveCategories(CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<Category> categories = await _xtreamClient.GetLiveCategoryAsync(plugin.Creds, cancellationToken).ConfigureAwait(false);
        return Ok(categories.Select(CreateCategoryResponse));
    }

    /// <summary>
    /// Get all Live TV streams for the given category.
    /// </summary>
    /// <param name="categoryId">The category id to fetch streams for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="ItemResponse"/> representing streams.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("LiveCategories/{categoryId}")]
    public async Task<ActionResult<IEnumerable<StreamInfo>>> GetLiveStreams(int categoryId, CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<StreamInfo> streams = await _xtreamClient.GetLiveStreamsByCategoryAsync(
          plugin.Creds,
          categoryId,
          cancellationToken).ConfigureAwait(false);
        return Ok(streams.Select(s => new ItemResponse { Id = s.StreamId, Name = s.Name, HasCatchup = s.TvArchive, CatchupDuration = s.TvArchiveDuration }));
    }

    /// <summary>
    /// Get all VOD categories from the Xtream provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="CategoryResponse"/>.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("VodCategories")]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> GetVodCategories(CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<Category> categories = await _xtreamClient.GetVodCategoryAsync(plugin.Creds, cancellationToken).ConfigureAwait(false);
        return Ok(categories.Select(CreateCategoryResponse));
    }

    /// <summary>
    /// Get all VOD streams for the given category.
    /// </summary>
    /// <param name="categoryId">The category id to fetch streams for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="ItemResponse"/> representing VOD streams.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("VodCategories/{categoryId}")]
    public async Task<ActionResult<IEnumerable<StreamInfo>>> GetVodStreams(int categoryId, CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<StreamInfo> streams = await _xtreamClient.GetVodStreamsByCategoryAsync(
          plugin.Creds,
          categoryId,
          cancellationToken).ConfigureAwait(false);
        return Ok(streams.Select(s => new ItemResponse { Id = s.StreamId, Name = s.Name, HasCatchup = s.TvArchive, CatchupDuration = s.TvArchiveDuration }));
    }

    /// <summary>
    /// Get all Series categories from the Xtream provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="CategoryResponse"/>.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("SeriesCategories")]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> GetSeriesCategories(CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<Category> categories = await _xtreamClient.GetSeriesCategoryAsync(plugin.Creds, cancellationToken).ConfigureAwait(false);
        return Ok(categories.Select(CreateCategoryResponse));
    }

    /// <summary>
    /// Get all Series streams for the given category.
    /// </summary>
    /// <param name="categoryId">The category id to fetch series for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="ItemResponse"/> representing series entries.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("SeriesCategories/{categoryId}")]
    public async Task<ActionResult<IEnumerable<StreamInfo>>> GetSeriesStreams(int categoryId, CancellationToken cancellationToken)
    {
        Plugin plugin = Plugin.Instance;
        List<Series> series = await _xtreamClient.GetSeriesByCategoryAsync(
          plugin.Creds,
          categoryId,
          cancellationToken).ConfigureAwait(false);
        return Ok(series.Select(s => new ItemResponse { Id = s.SeriesId, Name = s.Name, HasCatchup = false, CatchupDuration = 0 }));
    }

    /// <summary>
    /// Get all configured TV channels from the plugin's StreamService.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>An enumerable of <see cref="ChannelResponse"/>.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpGet("LiveTv")]
    public async Task<ActionResult<IEnumerable<StreamInfo>>> GetLiveTvChannels(CancellationToken cancellationToken)
    {
        IEnumerable<StreamInfo> streams = await Plugin.Instance.StreamService.GetLiveStreams(cancellationToken).ConfigureAwait(false);
        var channels = streams.Select(s => new ChannelResponse { Id = s.StreamId, LogoUrl = s.StreamIcon, Name = s.Name, Number = s.Num }).ToList();
        return Ok(channels);
    }
}
