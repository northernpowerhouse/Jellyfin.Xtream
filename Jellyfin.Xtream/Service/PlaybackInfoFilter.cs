using System;
using System.Threading.Tasks;
using Jellyfin.Xtream;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Service
{
    /// <summary>
    /// MVC result filter that intercepts PlaybackInfo responses and ensures MediaSources
    /// point at server-local restream URLs when available.
    /// </summary>
    public class PlaybackInfoFilter : IAsyncResultFilter
    {
        private readonly RestreamManager _restreamManager;
        private readonly ILogger<PlaybackInfoFilter> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackInfoFilter"/> class.
        /// </summary>
        /// <param name="restreamManager">The restream manager.</param>
        /// <param name="logger">The logger.</param>
        public PlaybackInfoFilter(RestreamManager restreamManager, ILogger<PlaybackInfoFilter> logger)
        {
            _restreamManager = restreamManager ?? throw new ArgumentNullException(nameof(restreamManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Inspect the action result and adjust PlaybackInfo MediaSources when necessary.
        /// </summary>
        /// <param name="context">The result executing context.</param>
        /// <param name="next">The next delegate in the pipeline.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            // Only run this filter for PlaybackInfo endpoints to avoid touching unrelated responses.
            try
            {
                var routeValues = context.ActionDescriptor?.RouteValues;
                var path = context.HttpContext?.Request?.Path.Value ?? string.Empty;

                var isPlaybackInfoRoute = false;

                if (routeValues != null)
                {
                    routeValues.TryGetValue("controller", out var controller);
                    routeValues.TryGetValue("action", out var action);

                    if (!string.IsNullOrEmpty(controller) && controller.Equals("MediaInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(action) && action.Contains("PlaybackInfo", StringComparison.OrdinalIgnoreCase))
                        {
                            isPlaybackInfoRoute = true;
                        }
                    }
                }

                // Also allow matching by path containing /PlaybackInfo
                if (!isPlaybackInfoRoute && !string.IsNullOrEmpty(path) && path.Contains("/PlaybackInfo", StringComparison.OrdinalIgnoreCase))
                {
                    isPlaybackInfoRoute = true;
                }

                if (!isPlaybackInfoRoute)
                {
                    // Not a PlaybackInfo request - skip.
                    await next().ConfigureAwait(false);
                    return;
                }

                // Always attempt to restream eligible external MediaSources. This filter
                // will rewrite MediaSource paths to server-local LiveStreamFiles URLs when
                // possible so clients can stream via the server.
            }
            catch (Exception)
            {
                // If anything goes wrong determining route, fall back to running the filter.
            }

            try
            {
                if (context.Result is ObjectResult obj && obj.Value != null)
                {
                    // Inspecting PlaybackInfo result for possible restreaming
                    // PlaybackInfoResponse has a MediaSources property - use reflection so we don't need a direct compile-time dependency
                    var playbackInfo = obj.Value;
                    var piType = playbackInfo.GetType();
                    var mediaSourcesProp = piType.GetProperty("MediaSources");

                    if (mediaSourcesProp != null)
                    {
                        var mediaSources = mediaSourcesProp.GetValue(playbackInfo) as System.Collections.IEnumerable;
                        if (mediaSources != null)
                        {
                            foreach (var ms in mediaSources)
                            {
                                if (ms == null)
                                {
                                    continue;
                                }

                                string? id = null;

                                try
                                {
                                    var msType = ms.GetType();
                                    var idProp = msType.GetProperty("Id");
                                    var pathProp = msType.GetProperty("Path");
                                    var encoderProp = msType.GetProperty("EncoderPath");

                                    id = idProp?.GetValue(ms)?.ToString();
                                    var path = pathProp?.GetValue(ms)?.ToString();

                                    // Only attempt restreaming for external HTTP/HTTPS URLs that are not already
                                    // a server-local LiveStreamFiles path. This avoids touching local file/library items.
                                    var tryRestream = false;
                                    if (!string.IsNullOrEmpty(path) && Uri.TryCreate(path, UriKind.Absolute, out var uri))
                                    {
                                        var scheme = uri.Scheme.ToLowerInvariant();
                                        if ((scheme == "http" || scheme == "https")
                                            && !uri.AbsolutePath.Contains("/LiveTv/LiveStreamFiles/", StringComparison.OrdinalIgnoreCase)
                                            && !uri.IsLoopback)
                                        {
                                            tryRestream = true;
                                        }
                                    }

                                    if (!tryRestream)
                                    {
                                        continue;
                                    }

                                    // Build a DTO that RestreamManager understands and ask it to ensure a restream
                                    var dto = new MediaBrowser.Model.Dto.MediaSourceInfo()
                                    {
                                        Id = id,
                                        Path = path
                                    };

                                    var ensured = _restreamManager.EnsureRestream(dto);

                                    // If EnsureRestream returned a server-local Path, copy it back into the response's MediaSource
                                    if (!string.IsNullOrEmpty(ensured?.Path) && ensured.Path != path)
                                    {
                                        pathProp?.SetValue(ms, ensured.Path);
                                        if (encoderProp != null)
                                        {
                                            encoderProp.SetValue(ms, ensured.EncoderPath);
                                        }
                                    }
                                }
                                catch (Exception)
                                {
                                    // ignore media source processing errors
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore failures during filter processing - don't block the response
                _ = ex;
            }

            await next().ConfigureAwait(false);
        }
    }
}
