// Copyright (C) 2022  Kevin Jilissen

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Net.Http;
using Jellyfin.Xtream.Client;
using Jellyfin.Xtream.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Core services
        serviceCollection.AddSingleton<IXtreamClient, XtreamClient>();
        serviceCollection.AddSingleton<ILiveTvService, LiveTvService>();

        // API Controllers
        serviceCollection.AddScoped<Api.XtreamController>();

        // Channels
        serviceCollection.AddSingleton<IChannel, CatchupChannel>();
        serviceCollection.AddSingleton<IChannel, SeriesChannel>();
        serviceCollection.AddSingleton<IChannel, VodChannel>();

        // Providers
        serviceCollection.AddSingleton<IPreRefreshProvider, XtreamVodProvider>();

        // Restream manager - manages server-local restream instances for proxied playback
        serviceCollection.AddSingleton<Service.RestreamManager>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<Service.RestreamManager>>();
            return new Service.RestreamManager(applicationHost, httpFactory, logger);
        });

        // Register the PlaybackInfo result filter and configure MVC to use it so we can
        // replace PlaybackInfo MediaSource paths with server-local restream URLs.
        serviceCollection.AddScoped<Service.PlaybackInfoFilter>();
        serviceCollection.Configure<MvcOptions>(opts =>
        {
            // Use ServiceFilter so the filter is created via DI and can receive RestreamManager
            opts.Filters.Add(new ServiceFilterAttribute(typeof(Service.PlaybackInfoFilter)));
        });
    }
}
