import { describe, expect, it } from "bun:test";
import { filterDestinationsByAudioLanguage } from "../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeAudioDestinations";
import type { StreamingDestination } from "@cantaro/client-shared/media";

describe("episode destination audio filtering", () => {
  it("keeps exact Stremio and AIOStreams episode links for player-side language selection", () => {
    const destinations: StreamingDestination[] = [
      {
        serviceId: "crunchyroll",
        displayName: "Crunchyroll",
        kind: "episode",
        url: "https://www.crunchyroll.com/watch/JA/episode",
        audioLocale: "ja-JP",
      },
      {
        serviceId: "crunchyroll",
        displayName: "Crunchyroll",
        kind: "episode",
        url: "https://www.crunchyroll.com/watch/EN/episode",
        audioLocale: "en-US",
      },
      {
        serviceId: "stremio",
        displayName: "Stremio",
        kind: "episode",
        url: "stremio:///detail/series/tt0108778/tt0108778:1:1",
      },
      {
        serviceId: "aiostreams",
        displayName: "AIOStreams",
        kind: "episode",
        url: "https://aio.example/jellyfin/web/#/open?type=series&metaId=tt0108778&videoId=tt0108778%3A1%3A1",
      },
    ];

    expect(filterDestinationsByAudioLanguage(destinations, "en")).toEqual([
      destinations[1],
      destinations[2],
      destinations[3],
    ]);
  });

  it("returns all destinations when no audio language is selected", () => {
    const destinations: StreamingDestination[] = [];

    expect(filterDestinationsByAudioLanguage(destinations, null)).toBe(destinations);
  });
});
