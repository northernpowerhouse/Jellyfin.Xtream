using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Service;

/// <summary>
/// Manages Restream instances used to proxy upstream streams to server-local URLs.
/// </summary>
public class RestreamManager
{
    private readonly IServerApplicationHost _appHost;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Restream> _restreams = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RestreamManager"/> class.
    /// </summary>
    /// <param name="appHost">Server application host.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public RestreamManager(IServerApplicationHost appHost, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(appHost);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _appHost = appHost;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensure a restream exists for the provided media source. The Restream constructor will modify
    /// the provided MediaSource.Path to a server-local path which can be returned in PlaybackInfo.
    /// </summary>
    /// <param name="mediaSource">The media source to restream.</param>
    /// <returns>The media source (modified by Restream constructor).</returns>
    public MediaBrowser.Model.Dto.MediaSourceInfo EnsureRestream(MediaBrowser.Model.Dto.MediaSourceInfo mediaSource)
    {
        ArgumentNullException.ThrowIfNull(mediaSource);

        // Use OriginalStreamId as key if set, otherwise use MediaSource.Id
        string key = mediaSource.Id ?? Guid.NewGuid().ToString();

        // If a restream already exists for this id, return the mediaSource (which was already modified)
        if (_restreams.TryGetValue(key, out var existing) && existing != null)
        {
            // If a Restream instance already exists for this key, copy its server-local
            // MediaSource mapping into the provided DTO so callers receive the proxied URL
            // even when they construct a fresh MediaSourceInfo instance.
            try
            {
                if (existing.MediaSource != null)
                {
                    mediaSource.Path = existing.MediaSource.Path;
                    mediaSource.EncoderPath = existing.MediaSource.EncoderPath;
                    mediaSource.LiveStreamId = existing.MediaSource.LiveStreamId;
                    mediaSource.RequiresOpening = existing.MediaSource.RequiresOpening;
                    try
                    {
                        mediaSource.SupportsDirectPlay = existing.MediaSource.SupportsDirectPlay;
                        mediaSource.SupportsDirectStream = existing.MediaSource.SupportsDirectStream;
                        mediaSource.SupportsTranscoding = existing.MediaSource.SupportsTranscoding;
                        mediaSource.UseMostCompatibleTranscodingProfile = existing.MediaSource.UseMostCompatibleTranscodingProfile;
                        mediaSource.SupportsProbing = existing.MediaSource.SupportsProbing;
                    }
                    catch
                    {
                        // Best-effort: ignore if properties aren't available on DTOs
                    }
                }
            }
            catch
            {
                // Best-effort: ignore any failures copying values.
            }

            return mediaSource;
        }

        // Create new Restream which will modify mediaSource.Path to server-local url
        var restream = new Restream(_appHost, _httpClientFactory, _logger, mediaSource);

        if (_restreams.TryAdd(key, restream))
        {
            // Fire-and-forget open; Restream will start copying data into its buffer.
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await restream.Open(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to open restream for {Key}", key);
                    }
                },
                CancellationToken.None);
        }

        return mediaSource;
    }

    /// <summary>
    /// Get or create a Restream instance for the provided media source.
    /// Returns the Restream that will be used as an ILiveStream implementation.
    /// </summary>
    /// <param name="mediaSource">The media source to restream.</param>
    /// <returns>The Restream instance associated with the media source.</returns>
    public Restream GetOrCreateRestream(MediaBrowser.Model.Dto.MediaSourceInfo mediaSource)
    {
        ArgumentNullException.ThrowIfNull(mediaSource);

        string key = mediaSource.Id ?? Guid.NewGuid().ToString();

        if (_restreams.TryGetValue(key, out var existing) && existing != null)
        {
            return existing;
        }

        var restream = new Restream(_appHost, _httpClientFactory, _logger, mediaSource);

        if (_restreams.TryAdd(key, restream))
        {
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await restream.Open(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to open restream for {Key}", key);
                    }
                },
                CancellationToken.None);
        }

        return restream;
    }
}
