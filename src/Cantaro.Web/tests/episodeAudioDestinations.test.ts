import { describe, expect, it } from "bun:test";
import { filterDestinationsByAudioLanguage } from "../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeAudioDestinations";
import type { StreamingDestination } from "@cantaro/client-shared/media";

describe("episode destination audio filtering", () => {
  it("keeps exact Stremio episode links for player-side language selection", () => {
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
    ];

    expect(filterDestinationsByAudioLanguage(destinations, "en")).toEqual([
      destinations[1],
      destinations[2],
    ]);
  });

  it("returns all destinations when no audio language is selected", () => {
    const destinations: StreamingDestination[] = [];

    expect(filterDestinationsByAudioLanguage(destinations, null)).toBe(destinations);
  });
});
