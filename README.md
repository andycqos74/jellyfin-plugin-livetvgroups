# Live TV Groups — a Jellyfin plugin

Serves the `group-title` categories from your M3U playlists over the API, so a client
can browse Live TV by category instead of one flat list of every channel.

## Why this exists

Jellyfin already parses `group-title`. `M3uParser` puts it in `ChannelInfo.ChannelGroup`
— and nothing ever reads it again. When the guide refresh creates the library item,
`GuideManager.GetChannel` copies only `ChannelInfo.Tags`, and the M3U parser never sets
`Tags`. There is no `GET` on `/LiveTv/TunerHosts` either, so a client cannot even find
the playlist to parse it itself.

The grouping therefore exists in your playlist and nowhere in the API. This plugin reads
the playlists back and puts it there.

## What it does

1. Reads the M3U tuner hosts from the Live TV configuration (no extra setup — it uses the
   tuners already configured under **Dashboard → Live TV**).
2. Fetches and parses each playlist, taking `group-title`, the channel name and the
   channel number.
3. Matches every entry to the Live TV channel Jellyfin created from it, and returns the
   groups keyed to Jellyfin channel ids.

Name derivation deliberately mirrors Jellyfin's own `M3uParser.GetChannelName` — the text
after the last comma with a leading `84.` style number stripped, then `tvg-name`, then
`tvg-id` — so the name being matched is the same string Jellyfin stored on the item, not
an approximation. Channel number is the fallback when a name has since been edited
server-side. Ambiguous matches are skipped rather than guessed at.

Channels no playlist entry matched are collected into a final catch-all group (default
name **Ungrouped**), so a client that browses by group can still reach everything.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/LiveTvGroups/Groups` | groups with their channel ids, plus match counters |
| POST | `/LiveTvGroups/Refresh` | admin — drop the cached playlist |

`GET /LiveTvGroups/Groups` needs only normal Jellyfin authentication: it returns group
names and channel ids, never the playlist URL or any tuner credentials.

```json
{
  "Groups": [
    { "Name": "UK Entertainment", "ChannelIds": ["f1e2...", "a3b4..."] },
    { "Name": "UK Sport",         "ChannelIds": ["c5d6..."] }
  ],
  "TotalChannels": 322,
  "MatchedChannels": 318,
  "PlaylistEntries": 322,
  "PlaylistCount": 1,
  "Errors": [],
  "GeneratedAt": "2026-09-13T21:47:00Z"
}
```

The counters are there so "no playlist configured" and "playlist configured but nothing
matched" can be told apart without reading the server log. A playlist that fails to load
is reported in `Errors` and never empties the response.

## Settings

| Setting | Default | Notes |
| --- | --- | --- |
| Cache minutes | 60 | Playlists change rarely and can be megabytes. 0 disables caching. |
| Download timeout | 60s | Per playlist. |
| Name for ungrouped channels | `Ungrouped` | Also collects channels nothing matched. |
| Sort groups by name | off | Off keeps playlist order, which is usually deliberate. |

## Building

Needs the **.NET 9 SDK**.

```bash
dotnet publish Jellyfin.Plugin.LiveTvGroups/Jellyfin.Plugin.LiveTvGroups.csproj -c Release -o out
```

Copy `Jellyfin.Plugin.LiveTvGroups.dll` into `plugins/LiveTvGroups_1.0.0.0/` in the
Jellyfin data directory and restart the server.

## Limitations

- M3U tuners only. HDHomeRun and other tuner types carry no `group-title` to read.
- The playlist has to be reachable from the Jellyfin server, which it already is — the
  server is what streams from it.
- Groups are matched per channel, not per stream: a channel appearing in two groups lands
  in the first one the playlist lists.
