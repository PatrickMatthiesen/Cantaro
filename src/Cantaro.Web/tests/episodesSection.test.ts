import { expect, it } from "bun:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { EpisodesSection } from "../../Cantaro.ClientShared/src/media/pages/media-entry-detail/EpisodesSection";
import {
  filterDestinationsByAudioLanguage,
  getDestinationAudioLanguages,
} from "../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeAudioDestinations";
import type {
  MediaEntryDetailModel,
  MediaEpisodeCatalogDto,
  MediaStreamingDestinationDto,
} from "../../Cantaro.ClientShared/src/media/services/mediaApi";
import { resolveStreamingDestinations } from "../../Cantaro.ClientShared/src/media/services/streamingDestinations";

const episodeTwoEnglish = "https://www.crunchyroll.com/watch/OPAQUEEN";
const episodeTwoJapanese = "https://www.crunchyroll.com/watch/OPAQUEJA";
const episodeOneEnglish = "https://www.crunchyroll.com/watch/EXPLICITDUB";
const episodeOneJapanese = "https://www.crunchyroll.com/watch/EXPLICITSUB";

it("uses explicit audio evidence for subtitled streams and falls back to Japanese when audio is unknown", () => {
  const koreanUrl = "https://www.crunchyroll.com/watch/KOREANSUB";
  const spanishUrl = "https://www.crunchyroll.com/watch/SPANISHSUB";
  const catalog: MediaEpisodeCatalogDto = {
    seriesDestinations: [],
    specials: [],
    episodes: [{
      episodeNumber: 1,
      destinations: [
        { ...createDestination(koreanUrl, "sub:en"), audioLocale: "ko-KR" },
        createDestination(spanishUrl, "sub:en", "es"),
        createDestination(episodeOneJapanese, "sub:en"),
      ],
    }],
  };
  const destinations = resolveStreamingDestinations([], catalog, "crunchyroll").episodes[0]!.destinations;
  expect(getDestinationAudioLanguages(destinations.find(destination => destination.url === koreanUrl)!)).toEqual(["ko"]);
  expect(filterDestinationsByAudioLanguage(destinations, "ja").map(destination => destination.url)).toEqual([episodeOneJapanese]);
  expect(filterDestinationsByAudioLanguage(destinations, "ko").map(destination => destination.url)).toEqual([koreanUrl]);
});

function createDestination(
  url: string,
  releaseTrack: string | null,
  audioLanguage?: string,
): MediaStreamingDestinationDto {
  return {
    url,
    releaseTrack,
    audioLocale: null,
    availableAudioLanguageCodes: audioLanguage ? [audioLanguage] : [],
    availableSubtitleLanguageCodes: ["en"],
    serviceId: "crunchyroll",
  };
}

it.each([
  { audio: "English", track: "dub:en", first: episodeOneEnglish, second: episodeTwoEnglish },
  { audio: "Japanese", track: "sub:en", first: episodeOneJapanese, second: episodeTwoJapanese },
  { audio: "Default", track: undefined, first: episodeOneEnglish, second: episodeTwoJapanese },
])("renders one direct link per episode for $audio audio", ({ track, first, second }) => {
  const catalog: MediaEpisodeCatalogDto = {
    seriesDestinations: [],
    specials: [],
    episodes: [
      {
        episodeNumber: 1,
        destinations: [
          createDestination(episodeOneEnglish, "dub:en"),
          createDestination(episodeOneJapanese, "sub:en"),
        ],
      },
      {
        episodeNumber: 2,
        destinations: [
          createDestination(episodeTwoJapanese, null, "ja"),
          createDestination(episodeTwoEnglish, null, "en"),
        ],
      },
    ],
  };
  const streamingDestinations = resolveStreamingDestinations([], catalog, "crunchyroll");
  expect(streamingDestinations.episodes[1]?.destinations.map(destination => ({
    audio: destination.availableAudioLanguageCodes,
    subtitles: destination.availableSubtitleLanguageCodes,
  }))).toEqual([
    { audio: ["ja"], subtitles: ["en"] },
    { audio: ["en"], subtitles: ["en"] },
  ]);

  const markup = renderToStaticMarkup(createElement(EpisodesSection, {
    entry: { title: { id: "title", episodeCount: 2 } } as MediaEntryDetailModel,
    state: { status: "loaded", value: catalog },
    streamingDestinations,
    preferredServiceId: "crunchyroll",
    preferredMediaReleaseTrack: track,
    seasonOptions: [],
    selectedSeason: "all",
    onRefresh: () => {},
    onSelectSeason: () => {},
    onSelectStreamingService: () => {},
  }));

  const links = Array.from(markup.matchAll(/href="([^"]+)"/g), match => match[1]);
  expect(links).toEqual([first, second]);
});
