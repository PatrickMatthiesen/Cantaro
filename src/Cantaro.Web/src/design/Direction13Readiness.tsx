import {
  AlertTriangle,
  Check,
  CloudOff,
  ExternalLink,
  ImageOff,
  Library,
  Moon,
  RefreshCw,
  Save,
  Sun,
} from "lucide-react";

import {
  direction13Scenarios,
  type Direction13Scenario,
  type Direction13ScenarioId,
  type Direction13Theme,
} from "./Direction13Scenarios";

export function Direction13ReviewToolbar({
  theme,
  scenarioId,
  onTheme,
  onScenario,
}: {
  theme: Direction13Theme;
  scenarioId: Direction13ScenarioId;
  onTheme: (theme: Direction13Theme) => void;
  onScenario: (scenario: Direction13ScenarioId) => void;
}) {
  return (
    <section
      aria-label="Design review controls"
      className="flex flex-wrap items-center gap-3 border-y border-border-subtle py-2 text-sm"
    >
      <span className="px-2 font-semibold text-content">Review</span>
      <div
        role="group"
        aria-label="Preview theme"
        className="flex border border-border-subtle"
      >
        <button
          type="button"
          aria-pressed={theme === "light"}
          onClick={() => onTheme("light")}
          className={`inline-flex min-h-9 items-center gap-2 px-3 font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-personal-accent ${theme === "light" ? "bg-surface-raised text-content" : "text-content-muted hover:bg-surface-hover hover:text-content"}`}
        >
          <Sun size={16} /> Light
        </button>
        <button
          type="button"
          aria-pressed={theme === "dark"}
          onClick={() => onTheme("dark")}
          className={`inline-flex min-h-9 items-center gap-2 px-3 font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-personal-accent ${theme === "dark" ? "bg-surface-raised text-content" : "text-content-muted hover:bg-surface-hover hover:text-content"}`}
        >
          <Moon size={16} /> Dark
        </button>
      </div>
      <label className="ml-auto flex min-h-9 items-center gap-2 px-2 text-content-muted">
        Case
        <select
          value={scenarioId}
          onChange={(event) =>
            onScenario(event.target.value as Direction13ScenarioId)
          }
          className="min-h-9 max-w-[15rem] border border-border-subtle bg-canvas px-3 font-semibold text-content outline-none focus-visible:ring-2 focus-visible:ring-personal-accent"
        >
          {Object.values(direction13Scenarios).map((scenario) => (
            <option key={scenario.id} value={scenario.id}>
              {scenario.label}
            </option>
          ))}
        </select>
      </label>
    </section>
  );
}

const episodes = [
  {
    number: 22,
    title: "The Future of Enemies",
    state: "Watched",
    destination: "Crunchyroll · Sub / Dub",
  },
  {
    number: 23,
    title: "Conquering the Labyrinth",
    state: "Up next",
    destination: "Crunchyroll · Sub / Dub",
  },
  {
    number: 24,
    title: "Perfect Replicas",
    state: "Unwatched",
    destination: "Series destination",
  },
];

export function Direction13EpisodesPanel({
  scenario,
}: {
  scenario: Direction13Scenario;
}) {
  if (scenario.episodesState === "error")
    return (
      <section aria-labelledby="readiness-episodes">
        <h2 id="readiness-episodes" className="text-xl font-bold">
          Episodes
        </h2>
        <div
          role="alert"
          className="mt-5 flex flex-wrap items-center gap-4 bg-danger-surface p-5 text-danger-content"
        >
          <CloudOff size={22} />
          <div className="min-w-0 flex-1">
            <p className="font-bold">Episodes could not be refreshed</p>
            <p className="mt-1 text-sm">
              Your saved title and progress are still available. Try this
              section again when the provider responds.
            </p>
          </div>
          <button
            type="button"
            className="min-h-10 bg-danger-action px-4 font-bold text-danger-action-content hover:bg-danger-action-hover"
          >
            Try again
          </button>
        </div>
      </section>
    );
  return (
    <section aria-labelledby="readiness-episodes">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="readiness-episodes" className="text-xl font-bold">
            Episodes
          </h2>
          <p className="mt-1 text-sm text-content-muted">
            Destinations and language availability are resolved per episode.
          </p>
        </div>
        <button
          type="button"
          className="inline-flex min-h-10 items-center gap-2 px-3 text-sm font-semibold text-content-muted hover:bg-surface-hover hover:text-content"
        >
          <RefreshCw size={16} /> Refresh
        </button>
      </div>
      <ol className="mt-5 space-y-2">
        {episodes.map((episode) => (
          <li key={episode.number}>
            <a
              href="#readiness-episodes"
              className="group grid min-h-20 gap-3 px-3 py-4 transition-colors hover:bg-surface-hover sm:grid-cols-[3rem_minmax(0,1fr)_minmax(11rem,auto)] sm:items-center"
            >
              <span className="font-mono text-sm text-content-subtle">
                {episode.number}
              </span>
              <span className="min-w-0">
                <strong className="block truncate group-hover:text-personal-accent-strong">
                  {episode.title}
                </strong>
                <span
                  className={`mt-1 block text-xs ${episode.state === "Watched" ? "text-success-content" : episode.state === "Up next" ? "text-personal-accent-strong" : "text-content-muted"}`}
                >
                  {episode.state}
                </span>
              </span>
              <span className="text-sm text-content-muted group-hover:text-content">
                {episode.destination}
              </span>
            </a>
          </li>
        ))}
      </ol>
    </section>
  );
}

export function Direction13ProgressPanel({
  scenario,
}: {
  scenario: Direction13Scenario;
}) {
  const total = scenario.progressTotal;
  const synced = scenario.viewerState === "synced";
  const unsaved = scenario.viewerState === "unsaved";
  if (scenario.viewerState === "loading")
    return (
      <section aria-labelledby="readiness-progress">
        <h2 id="readiness-progress" className="text-xl font-bold">
          Your progress
        </h2>
        <p className="mt-1 text-sm text-content-muted">
          Loading private viewer state without blocking title information.
        </p>
        <div className="mt-6">
          <Direction13LoadingPanel />
        </div>
      </section>
    );
  if (scenario.viewerState === "error")
    return (
      <section aria-labelledby="readiness-progress">
        <h2 id="readiness-progress" className="text-xl font-bold">
          Your progress
        </h2>
        <div
          role="alert"
          className="mt-5 bg-warning-surface p-5 text-warning-content"
        >
          <div className="flex gap-3">
            <AlertTriangle className="shrink-0" />
            <div>
              <p className="font-bold">
                Private viewer state is temporarily unavailable
              </p>
              <p className="mt-1 text-sm">
                Public title information remains available. Refresh before
                making progress changes.
              </p>
            </div>
          </div>
          <button
            type="button"
            className="mt-4 min-h-10 bg-personal-accent px-4 font-bold text-personal-accent-content hover:bg-personal-accent-hover"
          >
            Refresh viewer state
          </button>
        </div>
      </section>
    );
  if (scenario.viewerState === "not-in-library")
    return (
      <section aria-labelledby="readiness-progress">
        <h2 id="readiness-progress" className="text-xl font-bold">
          Add to your library
        </h2>
        <p className="mt-2 max-w-[62ch] text-content-muted">
          Choose an initial status before Cantaro starts tracking this title.
        </p>
        <div className="mt-5 flex flex-wrap gap-3">
          <select
            aria-label="Initial library status"
            className="min-h-11 bg-surface-subtle px-4 text-content outline-none focus-visible:ring-2 focus-visible:ring-personal-accent"
          >
            <option>Planning</option>
            <option>Watching</option>
            <option>Paused</option>
          </select>
          <button
            type="button"
            className="inline-flex min-h-11 items-center gap-2 bg-personal-accent px-5 font-bold text-personal-accent-content hover:bg-personal-accent-hover"
          >
            <Library size={17} /> Add to library
          </button>
        </div>
      </section>
    );
  return (
    <section aria-labelledby="readiness-progress">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 id="readiness-progress" className="text-xl font-bold">
            Your progress
          </h2>
          <p className="mt-1 text-sm text-content-muted">
            {scenario.progressUnit === "chapters"
              ? "Reading progress"
              : "Watching progress"}
            {total === null ? " · final total unknown" : ""}
          </p>
        </div>
        <span
          className={`inline-flex items-center gap-2 text-sm font-semibold ${synced ? "text-success-content" : "text-warning-content"}`}
        >
          {synced ? <Check size={16} /> : <AlertTriangle size={16} />}
          {synced ? "Saved in Cantaro" : "Local changes not saved"}
        </span>
      </div>
      <label className="mt-7 block">
        <span className="flex justify-between text-sm">
          <span className="capitalize">{scenario.progressUnit}</span>
          <strong>
            {scenario.progressCurrent}
            {total === null ? "" : ` / ${total}`}
          </strong>
        </span>
        <input
          type="range"
          min={0}
          max={total ?? Math.max(150, scenario.progressCurrent + 10)}
          defaultValue={scenario.progressCurrent}
          className="mt-4 w-full accent-personal-accent"
        />
      </label>
      <div className="mt-6 flex flex-wrap gap-3">
        <button
          type="button"
          disabled={!unsaved}
          className="inline-flex min-h-11 items-center gap-2 bg-personal-accent px-5 font-bold text-personal-accent-content hover:bg-personal-accent-hover disabled:cursor-not-allowed disabled:opacity-45"
        >
          <Save size={17} /> Save progress
        </button>
        <button
          type="button"
          className="inline-flex min-h-11 items-center gap-2 bg-surface-subtle px-4 font-semibold text-content hover:bg-surface-hover"
        >
          <RefreshCw size={17} /> Refresh
        </button>
      </div>
    </section>
  );
}

export function Direction13ProviderManagementPanel() {
  return (
    <section aria-labelledby="readiness-providers">
      <div>
        <h2 id="readiness-providers" className="text-xl font-bold">
          Provider identities
        </h2>
        <p className="mt-1 max-w-[68ch] text-sm text-content-muted">
          Linked identities support availability checks and sync. Streaming
          destinations remain separate actionable endpoints.
        </p>
      </div>
      <dl className="mt-6 space-y-3">
        <div className="flex flex-wrap items-center gap-4 bg-surface-subtle p-4">
          <span className="inline-flex size-10 items-center justify-center bg-surface-subtle font-black text-content">
            A
          </span>
          <div className="min-w-0 flex-1">
            <dt className="font-bold">AniList</dt>
            <dd className="text-sm text-success-content">
              Linked · refreshed 12 minutes ago
            </dd>
          </div>
          <button
            type="button"
            className="min-h-10 px-3 text-sm font-semibold text-content-muted hover:bg-surface-hover hover:text-danger-content"
          >
            Unlink
          </button>
        </div>
        <div className="flex flex-wrap items-center gap-4 p-4">
          <span className="inline-flex size-10 items-center justify-center bg-surface-subtle font-black text-content-muted">
            T
          </span>
          <div className="min-w-0 flex-1">
            <dt className="font-bold">Trakt</dt>
            <dd className="text-sm text-content-muted">Not linked</dd>
          </div>
          <button
            type="button"
            className="min-h-10 bg-personal-accent px-4 text-sm font-bold text-personal-accent-content hover:bg-personal-accent-hover"
          >
            Link provider
          </button>
        </div>
      </dl>
      <a
        href="#readiness-providers"
        className="mt-5 inline-flex items-center gap-2 text-sm font-semibold text-content-muted hover:text-personal-accent-strong"
      >
        Manage all provider links <ExternalLink size={15} />
      </a>
    </section>
  );
}

function Direction13LoadingPanel() {
  return (
    <section
      aria-label="Loading title details"
      aria-busy="true"
      className="animate-pulse space-y-5 motion-reduce:animate-none"
    >
      <span className="block h-5 w-36 bg-surface-subtle" />
      <span className="block h-12 max-w-2xl bg-surface-subtle" />
      <span className="block h-4 max-w-lg bg-surface-subtle" />
      <span className="block h-24 max-w-3xl bg-surface-subtle" />
    </section>
  );
}

export function MissingArtwork({ compact = false }: { compact?: boolean }) {
  return (
    <div
      className={`flex items-center justify-center bg-surface-subtle text-content-muted ${compact ? "aspect-[2/3] w-full" : "h-72 w-48"}`}
    >
      <span className="flex flex-col items-center gap-2 text-center text-xs">
        <ImageOff size={24} /> Artwork unavailable
      </span>
    </div>
  );
}
