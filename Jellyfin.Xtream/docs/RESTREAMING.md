Restreaming (server-local proxied streams)

Overview

This document describes the approach used by the Jellyfin.Xtream plugin to "restream" external catchup streams through the Jellyfin server so PlaybackInfo returned to clients points at a server-local URL ("/LiveTv/LiveStreamFiles/{id}/stream.ts").

Design

- Restream: the class `Jellyfin.Xtream.Service.Restream` implements `ILiveStream` and `IDirectStreamProvider`. When constructed it:
  - Stores the original upstream URL (MediaSource.Path).
  - Allocates a WrappedBufferStream used as an internal circular buffer.
  - Rewrites the provided `MediaSourceInfo.Path` and `EncoderPath` to local server URLs:
    - `MediaSource.Path` = `<server-smart-api-url>/LiveTv/LiveStreamFiles/{unique}/stream.ts`
    - `MediaSource.EncoderPath` = `<server-local-api-url>/LiveTv/LiveStreamFiles/{unique}/stream.ts`
  - Marks the media source as local/live where possible (e.g. LiveStreamId, RequiresOpening) so Jellyfin treats it as a local live stream.
  - When opened, it establishes an HTTP upstream request and copies bytes into the buffer; consumers read from the buffer via a WrappedBufferReadStream.

- RestreamManager: `Jellyfin.Xtream.Service.RestreamManager` tracks Restream instances by a key (MediaSource.Id or OriginalStreamId). It provides two helpful APIs:
  - `EnsureRestream(MediaSourceInfo)` — ensures a Restream is registered for the given MediaSource. This method will mutate the provided `MediaSourceInfo` so its Path/EncoderPath point at the server-local URL and schedule background opening of the upstream connection.
  - `GetOrCreateRestream(MediaSourceInfo)` — returns the `Restream` instance (useful when callers require the ILiveStream instance directly).

- PlaybackInfoFilter: `Jellyfin.Xtream.Service.PlaybackInfoFilter` is an MVC result filter registered for the plugin. It inspects outgoing `PlaybackInfo` action results (without a direct compile-time dependency on the PlaybackInfo type) and:
  - Iterates the `MediaSources` array via reflection.
  - For external HTTP(s) URLs that are not already server-local (and are not loopback/local file URLs), builds a temporary `MediaSourceInfo` DTO and calls `RestreamManager.EnsureRestream(dto)`.
  - If the resulting DTO has a different `Path`, it writes the server-local `Path` / `EncoderPath` back into the playback response so clients will use the server-local restream.

## Client API examples (direct restream vs transcoded playback)

This section documents the actual API calls a client (or integration test) can use to:

- obtain a direct server-local restream URL (preferred when the client can play the stream as-is), and
- request a transcoded / proxied playback (when the client cannot play the source codec/container and needs the server to transcode).

Replace {SERVER}, {API_KEY}, {USER_ID} and {ITEM_ID} with your values. Server base URL should include scheme and port, e.g. http://localhost:8096.

1) Minimal PlaybackInfo (client requests playback info; server may return the original upstream URL or a restream URL depending on server state)

```bash
curl -s -X POST "{SERVER}/Items/{ITEM_ID}/PlaybackInfo" \
   -H "Authorization: MediaBrowser Token=\"{API_KEY}\"" \
   -H "Content-Type: application/json" \
   -d '{"UserId":"{USER_ID}"}'
```

Look for the first MediaSource.Path in the returned JSON. If the plugin has already ensured a restream instance, you should see a server-local path like:

`http://<jellyfin-host>:8096/LiveTv/LiveStreamFiles/{uuid}/stream.ts`

If the response contains the original external upstream (e.g. http://gmpro.org/...), request the proxied variant below.

2) Proxied PlaybackInfo (discourage direct play so Jellyfin will choose a server-local proxied path)

```bash
curl -s -X POST "{SERVER}/Items/{ITEM_ID}/PlaybackInfo" \
   -H "Authorization: MediaBrowser Token=\"{API_KEY}\"" \
   -H "Content-Type: application/json" \
   -d '{
      "UserId":"{USER_ID}",
      "DeviceProfile":{
         "Name":"ProxyClient",
         "MaxStreamingBitrate":120000000,
         "DirectPlayProfiles":[],
         "TranscodingProfiles":[]
      },
      "AutoOpenLiveStream":true
   }'
```

This request sends a DeviceProfile that discourages direct-play. The server (with the plugin) will respond with a server-local restream path which clients can use directly. The returned MediaSource will include flags (SupportsDirectPlay, SupportsDirectStream, SupportsTranscoding) that indicate whether the server expects to transcode; when the restream is available we set SupportsTranscoding=false to prefer direct play.

3) Force proxied & transcoded PlaybackInfo (ask server to prepare a transcode)

If you want the server to transcode (for clients that cannot play the original codec/container), supply a DeviceProfile that includes a TranscodingProfiles entry. Example:

```bash
curl -s -X POST "{SERVER}/Items/{ITEM_ID}/PlaybackInfo" \
   -H "Authorization: MediaBrowser Token=\"{API_KEY}\"" \
   -H "Content-Type: application/json" \
   -d '{
      "UserId":"{USER_ID}",
      "DeviceProfile":{
         "Name":"ForceTranscode",
         "MaxStreamingBitrate":120000000,
         "DirectPlayProfiles":[],
         "TranscodingProfiles":[{"Container":"ts","VideoCodec":"h264","AudioCodec":"aac"}]
      },
      "AutoOpenLiveStream":true
   }'
```

When the server prepares a transcoded stream it will return a PlaybackInfo whose MediaSource fields indicate a transcoding `TranscodingUrl` (or a MediaSource with SupportsTranscoding=true). The client should then use the returned `TranscodingUrl` or the server-local restream URL that points to the transcoded segment.

4) GET variant

Some clients use GET to fetch PlaybackInfo. You can perform the equivalent GET including query parameters:

```bash
curl -s -X GET "{SERVER}/Items/{ITEM_ID}/PlaybackInfo?UserId={USER_ID}" \
   -H "Authorization: MediaBrowser Token=\"{API_KEY}\""
```

5) LiveStreamId fallback

When PlaybackInfo returns a `LiveStreamId`, clients can also request the server-local HLS manifest using the `live.m3u8` endpoint:

```
{SERVER}/videos/{ITEM_ID}/live.m3u8?LiveStreamId={LiveStreamId}&MediaSourceId={MediaSourceId}&api_key={API_KEY}
```

This is useful when PlaybackInfo variants include a `LiveStreamId` and you want the server to supply the final playable manifest.

Notes
- The plugin tries to preserve HDR by advertising restreamed MediaSources as non-transcodable where possible (SupportsTranscoding=false). That steers Jellyfin to direct-play the restream for capable clients, while still allowing clients to request a transcode profile when necessary.
- If you want a reproducible test, use the `jellyfin_catchup_test.py` script in the repository; it demonstrates the sequence of PlaybackInfo calls described above.
Configuration

- The setting `ForceDirectPlayCatchup` (exposed in the plugin configuration page) gates this behavior. When enabled, PlaybackInfoFilter will attempt restreaming for eligible external URLs. The UI currently uses `ApiClient.updatePluginConfiguration` which issues a POST to the plugin configuration endpoint. Using POST is the supported way for this plugin UI (PUT may return 405).

E2E verification steps

1. Build the plugin (from project root):
   dotnet build -c Release Jellyfin.Xtream
   (artifact: `bin/Release/net9.0/Jellyfin.Xtream.dll`)

2. Deploy the built DLL to the Jellyfin test container plugin folder (example workspace path):
   cp bin/Release/net9.0/Jellyfin.Xtream.dll /docker/jellyfinpr/plugins/Jellyfin.Xtream/Jellyfin.Xtream.dll

3. Restart the Jellyfin test container (container name used in this workspace: `jellyfin-pr-test`):
   docker restart jellyfin-pr-test

4. Validate basic plugin endpoints from the server admin UI (examples):
   - GET /Xtream/TestProvider -> should return 200
   - GET /Xtream/LiveCategories -> should return 200
   - GET /Plugins/{pluginId}/Configuration -> should return the plugin configuration JSON (verify ForceDirectPlayCatchup exists)
   - POST /Plugins/{pluginId}/Configuration -> use POST to update ForceDirectPlayCatchup (returns 204)

5. Run the catchup test harness
   - Run `/home/joe/projects/pvr.jellyfin-dev/jellyfin_catchup_test.py` which performs PlaybackInfo queries and validates that, when ForceDirectPlayCatchup=true, PlaybackInfo MediaSource.Path values are server-local (`/LiveTv/LiveStreamFiles/.../stream.ts`).

Notes

- The restreaming implementation intentionally avoids restreaming library/local file items and only operates on external HTTP/HTTPS upstreams.
- Restream instances are background-opened to reduce race conditions at PlaybackInfo time; callers that need an open ILiveStream can call `GetOrCreateRestream(...)` and await `Open`.
- The plugin UI uses POST for saving configuration. If you script configuration changes, use POST to `/Plugins/{pluginId}/Configuration`.

Contact

If you need the implementation details or want to extend the behavior (for example, enable restreaming for more types), inspect the classes:
- `Jellyfin.Xtream.Service.Restream`
- `Jellyfin.Xtream.Service.RestreamManager`
- `Jellyfin.Xtream.Service.PlaybackInfoFilter`

