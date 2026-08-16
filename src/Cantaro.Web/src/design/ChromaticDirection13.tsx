import {
  ExternalLink,
  Maximize2,
  Minus,
  MoreHorizontal,
  Play,
  Plus,
  Star,
  X,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import {
  Direction13EpisodesPanel,
  Direction13ProgressPanel,
  Direction13ProviderManagementPanel,
  Direction13ReviewToolbar,
  MissingArtwork,
} from "./Direction13Readiness";
import { Direction13Header, Direction13Switcher } from "./Direction13Header";
import {
  Direction13MobileNavigation,
  Direction13Sidebar,
} from "./Direction13Navigation";
import {
  direction13Scenarios,
  type Direction13Scenario,
  type Direction13ScenarioId,
  type Direction13Tab,
  type Direction13Theme,
} from "./Direction13Scenarios";

const cover =
  "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587-qQTzQnEJJ3oB.jpg";
const sequelCover =
  "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx182255-butzrqd4I0aC.jpg";
const miniCover =
  "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx170068-ijY3tCP8KoWP.jpg";
const banner =
  "https://s4.anilist.co/file/anilistcdn/media/anime/banner/154587-ivXNJ23SM1xB.jpg";

const detailTabs = [
  "Overview",
  "Episodes",
  "Progress",
  "Providers",
  "Franchise",
  "Characters",
  "Details",
];

function PosterDialog({
  dialogRef,
  title,
}: {
  dialogRef: React.RefObject<HTMLDialogElement | null>;
  title: string;
}) {
  return (
    <dialog
      ref={dialogRef}
      aria-label="Frieren poster"
      className="m-auto max-h-[92vh] max-w-[min(92vw,34rem)] bg-[#11141a] p-3 text-white backdrop:bg-black/88"
    >
      <div className="flex items-center justify-between pb-3">
        <span className="pl-1 text-sm font-semibold">Full poster</span>
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          aria-label="Close poster"
          className="inline-flex size-10 items-center justify-center text-white/65 hover:bg-white/10 hover:text-white"
        >
          <X size={20} />
        </button>
      </div>
      <img
        src={cover}
        alt={`${title} full poster`}
        className="max-h-[78vh] w-auto object-contain"
      />
    </dialog>
  );
}

function Hero({ scenario }: { scenario: Direction13Scenario }) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const actionLabel =
    scenario.viewerState === "not-in-library"
      ? "Add to library"
      : scenario.viewerState === "loading"
        ? "Finding destination…"
        : scenario.progressUnit === "chapters"
          ? "Open reading destinations"
          : `Play episode ${scenario.progressCurrent + 1}`;
  return (
    <section
      aria-labelledby="quiet-title"
      className="relative isolate overflow-hidden bg-[#d7dde3] text-[#111827] dark:bg-[#090b10] dark:text-white"
    >
      {scenario.artworkAvailable && (
        <div
          className="absolute inset-0 bg-cover bg-center opacity-50 dark:opacity-65"
          style={{ backgroundImage: `url(${banner})` }}
        />
      )}
      <div className="absolute inset-0 bg-linear-to-r from-[#dfe4e8]/95 via-[#d9dfe5]/72 to-[#c9d1d9]/20 dark:from-[#090b10] dark:via-[#090b10]/82 dark:to-[#090b10]/25" />
      <div className="absolute inset-0 bg-linear-to-t from-[#d5dbe1]/70 via-transparent to-[#d8dee4]/25 dark:from-[#090b10] dark:to-[#090b10]/45" />
      <button
        type="button"
        onClick={() => dialogRef.current?.showModal()}
        aria-label="View full poster"
        disabled={!scenario.artworkAvailable}
        className="group absolute inset-x-0 top-0 z-30 h-44 text-right disabled:pointer-events-none md:hidden"
      >
        <Maximize2
          size={18}
          className="absolute right-5 top-5 text-[#111827]/55 transition-all group-hover:scale-110 group-hover:text-[#111827] group-focus-visible:text-[#111827] motion-reduce:transition-none dark:text-white/55 dark:group-hover:text-white dark:group-focus-visible:text-white"
        />
      </button>
      <div className="relative z-20 flex min-h-[31rem] items-end gap-8 px-4 pb-9 pt-44 sm:px-7 md:pt-24 xl:px-9">
        {scenario.artworkAvailable ? (
          <button
            type="button"
            onClick={() => dialogRef.current?.showModal()}
            aria-label="View full poster"
            className="group relative hidden w-48 shrink-0 overflow-hidden bg-white/75 p-2 outline-none focus-visible:ring-2 focus-visible:ring-personal-accent md:block dark:bg-[#11141a] xl:w-56"
          >
            <img
              src={cover}
              alt={`${scenario.title} cover`}
              className="h-auto w-full object-contain transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 group-focus-visible:scale-[1.025] motion-reduce:transition-none"
            />
            <span className="absolute right-4 top-4 inline-flex size-9 items-center justify-center bg-black/70 text-white opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100">
              <Maximize2 size={16} />
            </span>
          </button>
        ) : (
          <div className="hidden shrink-0 md:block">
            <MissingArtwork />
          </div>
        )}
        <div className="min-w-0 max-w-3xl pb-1">
          <p className="font-semibold text-[#374151] dark:text-white/80">
            {scenario.formatLine}
          </p>
          <h1
            id="quiet-title"
            className="mt-3 text-4xl font-black leading-[1.04] tracking-[-0.04em] text-[#111827] sm:text-5xl dark:text-white xl:text-6xl"
          >
            {scenario.title}
          </h1>
          <p className="mt-4 text-sm text-[#4b5563] dark:text-white/60">
            {scenario.metadata}
          </p>
          <p className="mt-5 max-w-[70ch] text-base leading-7 text-[#374151] dark:text-white/80">
            {scenario.synopsis}
          </p>
          <div className="mt-7 flex flex-wrap gap-3">
            <button
              type="button"
              disabled={scenario.viewerState === "loading"}
              className="inline-flex min-h-12 items-center gap-2 bg-personal-accent px-5 font-bold text-personal-accent-content transition-colors hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-personal-accent disabled:cursor-wait disabled:opacity-60"
            >
              {scenario.viewerState === "not-in-library" ? (
                <Plus size={18} />
              ) : (
                <Play size={18} fill="currentColor" />
              )}{" "}
              {actionLabel}
            </button>
            <button
              type="button"
              aria-label="More title actions"
              className="inline-flex size-12 items-center justify-center text-[#111827]/60 hover:bg-black/8 hover:text-[#111827] dark:text-white/65 dark:hover:bg-white/10 dark:hover:text-white"
            >
              <MoreHorizontal size={20} />
            </button>
          </div>
        </div>
      </div>
      {scenario.artworkAvailable && (
        <PosterDialog dialogRef={dialogRef} title={scenario.title} />
      )}
    </section>
  );
}

function Progress({ scenario }: { scenario: Direction13Scenario }) {
  const total = scenario.progressTotal;
  const percent = total
    ? Math.min(100, (scenario.progressCurrent / total) * 100)
    : 0;
  const loading = scenario.viewerState === "loading";
  const unavailable = scenario.viewerState === "error";
  const notInLibrary = scenario.viewerState === "not-in-library";
  return (
    <section
      aria-labelledby="quiet-progress"
      className="flex flex-col gap-5 border-b border-border-subtle py-5 lg:flex-row lg:items-center"
    >
      <div className="shrink-0 lg:w-28">
        <h2
          id="quiet-progress"
          className="text-sm font-semibold text-content-muted"
        >
          Your progress
        </h2>
        <p className="mt-1 text-2xl font-black">
          <span className="text-personal-accent-strong">
            {unavailable || loading ? "—" : scenario.progressCurrent}
          </span>
          {total !== null && !unavailable && !loading && (
            <span className="text-base text-content-subtle"> / {total}</span>
          )}
        </p>
      </div>
      <div className="min-w-0 flex-1">
        <div className="grid grid-cols-[2.25rem_minmax(8rem,1fr)_2.25rem] items-center gap-2">
          <button
            type="button"
            aria-label={`Decrease completed ${scenario.progressUnit}`}
            disabled={
              unavailable ||
              loading ||
              notInLibrary ||
              scenario.progressCurrent === 0
            }
            className="inline-flex size-9 items-center justify-center text-content-muted hover:bg-danger-surface hover:text-danger-content focus-visible:outline-2 focus-visible:outline-personal-accent disabled:cursor-not-allowed disabled:opacity-35"
          >
            <Minus size={18} />
          </button>
          <div
            className="h-1.5 bg-surface-subtle"
            role="progressbar"
            aria-label={`${scenario.progressCurrent} ${scenario.progressUnit} completed${total === null ? "" : ` of ${total}`}`}
            aria-valuemin={0}
            aria-valuemax={total ?? undefined}
            aria-valuenow={
              unavailable || loading ? undefined : scenario.progressCurrent
            }
          >
            <div
              className="h-full bg-personal-accent transition-[width] motion-reduce:transition-none"
              style={{ width: `${percent}%` }}
            />
          </div>
          <button
            type="button"
            aria-label={`Increase completed ${scenario.progressUnit}`}
            disabled={
              unavailable ||
              loading ||
              notInLibrary ||
              (total !== null && scenario.progressCurrent >= total)
            }
            className="inline-flex size-9 items-center justify-center text-content-muted hover:bg-success-surface hover:text-success-content focus-visible:outline-2 focus-visible:outline-personal-accent disabled:cursor-not-allowed disabled:opacity-35"
          >
            <Plus size={18} />
          </button>
        </div>
        <div className="mx-11 mt-2 flex justify-between gap-4 text-xs text-content-muted">
          <span>
            {loading
              ? "Loading viewer state"
              : unavailable
                ? "Viewer state unavailable"
                : notInLibrary
                  ? "Add this title to track progress"
                  : `${scenario.progressCurrent} ${scenario.progressUnit} completed`}
          </span>
          <span>
            {total === null || unavailable || loading || notInLibrary
              ? ""
              : `${Math.max(0, total - scenario.progressCurrent)} remaining`}
          </span>
        </div>
      </div>
      <div className="flex flex-wrap items-end gap-x-5 gap-y-3 lg:shrink-0 lg:justify-end">
        <div>
          <p className="text-xs text-content-muted">Your score</p>
          <div className="mt-1 flex" aria-label="No score set">
            {Array.from({ length: 5 }, (_, index) => (
              <button
                key={index}
                type="button"
                aria-label={`Set score to ${index + 1} stars`}
                className="inline-flex size-6 items-center justify-center text-content-subtle transition-colors hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-personal-accent"
              >
                <Star size={16} />
              </button>
            ))}
          </div>
        </div>
        <label className="text-xs text-content-muted">
          Library status
          <select
            aria-label="Library status"
            defaultValue={notInLibrary ? "Planning" : "Watching"}
            className="mt-1 block min-h-9 border border-border-subtle bg-canvas px-3 text-sm font-semibold text-content outline-none hover:border-border-strong focus-visible:ring-2 focus-visible:ring-personal-accent"
          >
            <option>Watching</option>
            <option>Planning</option>
            <option>Paused</option>
            <option>Completed</option>
          </select>
        </label>
      </div>
    </section>
  );
}

function DetailNavigation({
  activeTab,
  onTab,
}: {
  activeTab: Direction13Tab;
  onTab: (tab: Direction13Tab) => void;
}) {
  return (
    <div className="border-b border-border-subtle">
      <div
        role="tablist"
        aria-label="Title details"
        className="flex min-w-0 flex-1 gap-7 overflow-x-auto"
      >
        {detailTabs.map((tab) => (
          <button
            type="button"
            role="tab"
            key={tab}
            aria-selected={activeTab === tab}
            aria-controls="direction-13-panel"
            onClick={() => onTab(tab as Direction13Tab)}
            className={`min-h-14 shrink-0 border-b-2 py-5 text-sm font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-personal-accent ${activeTab === tab ? "border-personal-accent text-content" : "border-transparent text-content-muted hover:text-content"}`}
          >
            {tab}
          </button>
        ))}
      </div>
    </div>
  );
}

function Providers() {
  const providers = [
    {
      name: "Crunchyroll",
      detail: "Subtitles and dub",
      href: "https://www.crunchyroll.com/",
      tone: "hover:bg-[#f47521]/10 hover:text-[#ff9a56]",
    },
    {
      name: "Netflix",
      detail: "Available in your region",
      href: "https://www.netflix.com/",
      tone: "hover:bg-[#e50914]/10 hover:text-[#ff6b72]",
    },
  ];
  return (
    <section id="providers" aria-labelledby="quiet-providers">
      <div className="flex items-baseline justify-between gap-4">
        <h2 id="quiet-providers" className="text-xl font-bold">
          Where to watch
        </h2>
        <a
          href="#providers"
          className="text-sm text-content-muted hover:text-personal-accent-strong"
        >
          All destinations
        </a>
      </div>
      <ul className="mt-4 grid gap-2 sm:grid-cols-2">
        {providers.map((provider) => (
          <li key={provider.name}>
            <a
              href={provider.href}
              target="_blank"
              rel="noreferrer"
              className={`group flex min-h-20 items-center gap-3 px-3 py-4 text-content transition-colors ${provider.tone}`}
            >
              <span className="inline-flex size-10 shrink-0 items-center justify-center bg-surface-subtle font-black">
                {provider.name[0]}
              </span>
              <span className="min-w-0 flex-1">
                <strong className="block text-content group-hover:text-current">
                  {provider.name}
                </strong>
                <span className="mt-1 block text-xs text-content-muted">
                  {provider.detail}
                </span>
              </span>
              <ExternalLink
                size={16}
                className="shrink-0 opacity-45 transition-opacity group-hover:opacity-100"
              />
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Franchise() {
  const entries = [
    {
      title: "Beyond Journey’s End",
      relation: "Main story · watching",
      image: cover,
    },
    {
      title: "Season 2",
      relation: "Sequel · next in order",
      image: sequelCover,
    },
    {
      title: "Mini Anime",
      relation: "Side story · 12 shorts",
      image: miniCover,
    },
  ];
  return (
    <section id="franchise" aria-labelledby="quiet-franchise">
      <div className="flex items-baseline justify-between gap-4">
        <h2 id="quiet-franchise" className="text-xl font-bold">
          Franchise order
        </h2>
        <a
          href="#franchise"
          className="text-sm text-content-muted hover:text-personal-accent-strong"
        >
          View full franchise
        </a>
      </div>
      <ol className="mt-5 flex gap-6 overflow-x-auto pb-3">
        {entries.map((entry, index) => (
          <li key={entry.title} className="group w-48 shrink-0">
            <div className="overflow-hidden">
              <img
                src={entry.image}
                alt=""
                className="aspect-[2/3] w-full object-cover transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 motion-reduce:transition-none"
              />
            </div>
            <div className="mt-3 flex gap-3">
              <span className="font-mono text-xs text-content-subtle">
                0{index + 1}
              </span>
              <span className="min-w-0">
                <strong className="block leading-5 transition-colors group-hover:text-personal-accent-strong">
                  {entry.title}
                </strong>
                <span className="mt-1 block text-xs text-content-muted">
                  {entry.relation}
                </span>
              </span>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

function Characters() {
  const people = [
    {
      name: "Frieren",
      role: "Main · Mage",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b176754-PCnpqIOkjhFk.png",
    },
    {
      name: "Fern",
      role: "Main · Mage",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b183965-uGFohBjlFoTp.png",
    },
    {
      name: "Stark",
      role: "Main · Warrior",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b184313-CQl6GSt4RSny.jpg",
    },
    {
      name: "Himmel",
      role: "Supporting · Hero",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b184311-wQFySqYXEqf1.png",
    },
    {
      name: "Heiter",
      role: "Supporting · Priest",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b184310-tiXvrq4FINXP.jpg",
    },
    {
      name: "Eisen",
      role: "Supporting · Warrior",
      image:
        "https://s4.anilist.co/file/anilistcdn/character/large/b184312-kxd5H6iOHIq4.png",
    },
  ];
  return (
    <section id="characters" aria-labelledby="quiet-characters">
      <div className="flex items-baseline justify-between">
        <h2 id="quiet-characters" className="text-xl font-bold">
          Main characters
        </h2>
        <a
          href="#characters"
          className="text-sm text-content-muted hover:text-personal-accent-strong"
        >
          See all
        </a>
      </div>
      <ul className="mt-5 grid grid-cols-2 gap-4 sm:grid-cols-3 xl:grid-cols-6">
        {people.map(({ name, role, image }) => (
          <li key={name} className="min-w-0">
            <a
              href="#characters"
              className="group relative block aspect-[3/4] overflow-hidden bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-personal-accent"
            >
              <img
                src={image}
                alt={name}
                className="h-full w-full object-cover object-top transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 motion-reduce:transition-none"
              />
              <span className="absolute inset-x-0 bottom-0 bg-linear-to-t from-black/90 via-black/55 to-transparent px-3 pb-3 pt-12 text-white">
                <strong className="block truncate transition-colors group-hover:text-[#ffd679]">
                  {name}
                </strong>
                <span className="block truncate text-xs text-white/70">
                  {role}
                </span>
              </span>
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Information({ scenario }: { scenario: Direction13Scenario }) {
  const facts = [
    ["Format", scenario.progressUnit === "chapters" ? "Manga" : "TV series"],
    ["Status", "Finished"],
    ["Started", "Sep 29, 2023"],
    [
      "Ended",
      scenario.progressUnit === "chapters" ? "Unknown" : "Mar 22, 2024",
    ],
    ["Original title", "葬送のフリーレン"],
  ];
  return (
    <aside className="py-9 xl:border-l xl:border-border-subtle xl:pl-9">
      <section aria-labelledby="quiet-information">
        <h2 id="quiet-information" className="text-xl font-bold">
          Information
        </h2>
        <dl className="mt-4 space-y-4 text-sm">
          {facts.map(([term, value]) => (
            <div key={term} className="grid grid-cols-[5.25rem_1fr] gap-3">
              <dt className="text-content-subtle">{term}</dt>
              <dd className="font-medium text-content">{value}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section aria-labelledby="quiet-connections" className="mt-10">
        <h2 id="quiet-connections" className="text-xl font-bold">
          Connections
        </h2>
        <dl className="mt-4 space-y-4 text-sm">
          <div className="flex justify-between gap-4">
            <dt className="text-content-muted">AniList</dt>
            <dd className="text-right text-success-content">
              Progress current · 12m
            </dd>
          </div>
          <div className="flex justify-between gap-4">
            <dt className="text-content-muted">Provider links</dt>
            <dd className="text-right text-content">2 verified</dd>
          </div>
        </dl>
      </section>
      <section aria-labelledby="quiet-release" className="mt-10">
        <h2 id="quiet-release" className="text-xl font-bold">
          Release state
        </h2>
        <p className="mt-4 text-sm text-content-muted">
          {scenario.progressUnit === "chapters"
            ? "No final chapter total is currently available."
            : "All 28 episodes have aired."}
        </p>
      </section>
    </aside>
  );
}

export function ChromaticDirection13() {
  const [compact, setCompact] = useState(false);
  const [theme, setTheme] = useState<Direction13Theme>(() =>
    document.documentElement.dataset.theme === "light" ? "light" : "dark",
  );
  const [scenarioId, setScenarioId] = useState<Direction13ScenarioId>("ready");
  const [activeTab, setActiveTab] = useState<Direction13Tab>("Overview");
  const originalTheme = useRef<string | null>(null);
  const navigationDialogRef = useRef<HTMLDialogElement | null>(null);
  const scenario = direction13Scenarios[scenarioId];

  useEffect(() => {
    originalTheme.current = document.documentElement.dataset.theme ?? null;
    return () => {
      if (originalTheme.current === null)
        delete document.documentElement.dataset.theme;
      else document.documentElement.dataset.theme = originalTheme.current;
    };
  }, []);
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const selectScenario = (nextScenario: Direction13ScenarioId) => {
    setScenarioId(nextScenario);
    if (
      [
        "unsaved",
        "viewer-loading",
        "not-in-library",
        "viewer-error",
        "manga",
      ].includes(nextScenario)
    )
      setActiveTab("Progress");
    else if (nextScenario === "episodes-error") setActiveTab("Episodes");
    else setActiveTab("Overview");
  };

  const panel =
    activeTab === "Episodes" ? (
      <Direction13EpisodesPanel scenario={scenario} />
    ) : activeTab === "Progress" ? (
      <Direction13ProgressPanel scenario={scenario} />
    ) : activeTab === "Providers" ? (
      <Direction13ProviderManagementPanel />
    ) : activeTab === "Franchise" ? (
      <Franchise />
    ) : activeTab === "Characters" ? (
      <Characters />
    ) : activeTab === "Details" ? (
      <Information scenario={scenario} />
    ) : (
      <div className="grid gap-x-9 xl:grid-cols-[minmax(0,1fr)_19rem]">
        <div className="min-w-0 space-y-14 py-9">
          <Providers />
          <Franchise />
          <Characters />
        </div>
        <Information scenario={scenario} />
      </div>
    );
  return (
    <main className="min-h-screen bg-canvas text-content [--color-border-strong:#c9cdd4] [--color-border-subtle:#e1e3e7] [--color-canvas:#f8f8f9] [--color-surface-hover:#e1e4e8] [--color-surface-raised:#f0f1f3] [--color-surface-subtle:#e9ebef] [--color-surface:#ffffff] dark:[--color-border-strong:#343d4b] dark:[--color-border-subtle:#202733] dark:[--color-canvas:#080b11] dark:[--color-surface-hover:#202936] dark:[--color-surface-raised:#161c26] dark:[--color-surface-subtle:#171e29] dark:[--color-surface:#10151e]">
      <div
        className={`mx-auto grid min-h-screen max-w-[1680px] transition-[grid-template-columns] duration-200 motion-reduce:transition-none sm:grid-cols-[4.75rem_minmax(0,1fr)] ${compact ? "lg:grid-cols-[4.75rem_minmax(0,1fr)]" : "lg:grid-cols-[13.5rem_minmax(0,1fr)]"}`}
      >
        <Direction13Sidebar
          compact={compact}
          onToggle={() => setCompact((value) => !value)}
        />
        <div className="min-w-0">
          <Direction13Header
            onOpenNavigation={() => navigationDialogRef.current?.showModal()}
          />
          <div className="space-y-3 px-4 py-5 sm:px-7 xl:px-9">
            <Direction13Switcher />
            <Direction13ReviewToolbar
              theme={theme}
              scenarioId={scenarioId}
              onTheme={setTheme}
              onScenario={selectScenario}
            />
          </div>
          <Hero scenario={scenario} />
          <div className="px-4 pb-16 sm:px-7 xl:px-9">
            <Progress scenario={scenario} />
            <DetailNavigation activeTab={activeTab} onTab={setActiveTab} />
            <div
              id="direction-13-panel"
              role="tabpanel"
              className={activeTab === "Overview" ? "" : "py-9"}
            >
              {panel}
            </div>
          </div>
        </div>
      </div>
      <Direction13MobileNavigation dialogRef={navigationDialogRef} />
    </main>
  );
}
