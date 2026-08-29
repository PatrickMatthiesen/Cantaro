import { describe, expect, it } from "bun:test";
import { resolvePreferredEpisodeAudioLanguage } from "../../Cantaro.ClientShared/src/media/pages/media-entry-detail/preferredEpisodeAudio";

describe("preferred episode audio", () => {
  it("selects the requested dub language when a localized destination exists", () => {
    expect(resolvePreferredEpisodeAudioLanguage("dub:en", ["ja", "en"])).toBe("en");
    expect(resolvePreferredEpisodeAudioLanguage("DUB:en-US", ["ja", "en"])).toBe("en");
  });

  it("falls back when the requested dub language is unavailable", () => {
    expect(resolvePreferredEpisodeAudioLanguage("dub:en", ["ja"])).toBeNull();
  });

  it("selects observed Japanese audio for the English subtitle preference", () => {
    expect(resolvePreferredEpisodeAudioLanguage("sub:en", ["ja", "en"])).toBe("ja");
    expect(resolvePreferredEpisodeAudioLanguage("sub:en", ["en"])).toBeNull();
  });
});
