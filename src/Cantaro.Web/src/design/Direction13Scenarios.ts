export type Direction13Theme = "light" | "dark";
export type Direction13Tab =
  | "Overview"
  | "Episodes"
  | "Progress"
  | "Providers"
  | "Franchise"
  | "Characters"
  | "Details";
export type Direction13ScenarioId =
  | "ready"
  | "unsaved"
  | "viewer-loading"
  | "not-in-library"
  | "viewer-error"
  | "manga"
  | "missing-artwork"
  | "episodes-error";
export type Direction13Scenario = {
  id: Direction13ScenarioId;
  label: string;
  title: string;
  formatLine: string;
  metadata: string;
  synopsis: string;
  progressUnit: "episodes" | "chapters";
  progressCurrent: number;
  progressTotal: number | null;
  viewerState: "synced" | "unsaved" | "loading" | "not-in-library" | "error";
  artworkAvailable: boolean;
  episodesState: "ready" | "error";
};

const sharedTv = {
  title: "Frieren: Beyond Journey’s End",
  formatLine: "TV series · Fantasy · Adventure",
  metadata: "2023 · 28 episodes · 24 min",
  progressUnit: "episodes" as const,
  progressTotal: 28,
  artworkAvailable: true,
};

export const direction13Scenarios: Record<
  Direction13ScenarioId,
  Direction13Scenario
> = {
  ready: {
    ...sharedTv,
    id: "ready",
    label: "TV · synced",
    synopsis:
      "After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.",
    progressCurrent: 22,
    viewerState: "synced",
    episodesState: "ready",
  },
  unsaved: {
    ...sharedTv,
    id: "unsaved",
    label: "TV · unsaved",
    synopsis:
      "After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.",
    progressCurrent: 23,
    viewerState: "unsaved",
    episodesState: "ready",
  },
  "viewer-loading": {
    ...sharedTv,
    id: "viewer-loading",
    label: "Viewer state loading",
    synopsis:
      "Public title information remains available while private library state and destinations are loading.",
    progressCurrent: 0,
    viewerState: "loading",
    episodesState: "ready",
  },
  "not-in-library": {
    ...sharedTv,
    id: "not-in-library",
    label: "Not in library",
    synopsis:
      "Public title information remains useful before this title has been added to the personal archive.",
    progressCurrent: 0,
    viewerState: "not-in-library",
    episodesState: "ready",
  },
  "viewer-error": {
    ...sharedTv,
    id: "viewer-error",
    label: "Viewer state unavailable",
    synopsis:
      "Title information stays visible even when private library progress cannot be refreshed.",
    progressCurrent: 0,
    viewerState: "error",
    episodesState: "ready",
  },
  manga: {
    id: "manga",
    label: "Manga · unknown total",
    title: "Frieren: Beyond Journey’s End",
    formatLine: "Manga · Adventure · Fantasy",
    metadata: "2019 · Chapter total unknown",
    synopsis:
      "An open-ended reading progress case with chapter-based tracking and no trustworthy final total.",
    progressUnit: "chapters",
    progressCurrent: 138,
    progressTotal: null,
    viewerState: "synced",
    artworkAvailable: true,
    episodesState: "ready",
  },
  "missing-artwork": {
    ...sharedTv,
    id: "missing-artwork",
    label: "Long title · no artwork",
    title:
      "Frieren – Nach dem Ende der Reise und die Erinnerungen, die über Generationen hinweg weiterleben",
    formatLine: "TV series · Internationale Metadatenprüfung",
    synopsis:
      "A deliberately expanded title and description verify wrapping, translation headroom, missing imagery, and resilient action placement at narrow widths.",
    progressCurrent: 22,
    viewerState: "synced",
    episodesState: "ready",
    artworkAvailable: false,
  },
  "episodes-error": {
    ...sharedTv,
    id: "episodes-error",
    label: "Episodes unavailable",
    synopsis:
      "The page remains useful while one independently loaded section needs recovery.",
    progressCurrent: 22,
    viewerState: "synced",
    episodesState: "error",
  },
};
