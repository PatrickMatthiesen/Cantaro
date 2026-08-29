import { ArrowDown, ArrowUp, RotateCcw, Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState, type ComponentType, type PointerEvent as ReactPointerEvent } from "react";
import { DetailArtwork } from "../components/media-entry-detail/EntryDisplayPrimitives";
import type { MediaFranchiseGraphDto, MediaFranchiseNodeDto } from "../services/mediaApi";
import { mediaFormatLabel, mediaKindLabel } from "../services/mediaFormatting";
import {
  buildFranchisePresentation,
  relationLabel,
  type FranchisePresentation,
} from "./media-entry-detail/franchiseGraph";
import { SquareOverlayNode } from "./franchise/SquareOverlayNode";
import type { FranchiseGraphState } from "./media-entry-detail/mediaEntryDetailTypes";

type FranchiseLayoutId = "release-horizontal" | "release-vertical" | "episode" | "format" | "table";
type FranchiseCardType = "compact" | "portrait" | "square" | "wide";

const layoutOptions: Array<{ id: FranchiseLayoutId; label: string }> = [
  { id: "release-horizontal", label: "By release →" },
  { id: "release-vertical", label: "By release ↓" },
  { id: "episode", label: "By episode" },
  { id: "format", label: "By format" },
  { id: "table", label: "Table" },
];

function nodeMeta(node: MediaFranchiseNodeDto) {
  return [
    node.mediaFormat ? mediaFormatLabel(node.mediaFormat) : mediaKindLabel(node.mediaKind),
    node.episodeCount ? `${node.episodeCount} ep.` : null,
    node.startYear,
  ].filter(Boolean).join(" · ");
}

function CompactNode({
  node,
  onNavigate,
  emphasis = false,
}: {
  node: MediaFranchiseNodeDto;
  onNavigate: (id: string) => void;
  emphasis?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={() => onNavigate(node.mediaTitleId)}
      className="group min-w-0 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <span className={`flex min-w-0 gap-3 border bg-surface p-3 transition-colors group-hover:border-content-subtle ${emphasis ? "border-focus" : "border-border-subtle"}`}>
        <span className="relative block h-24 w-16 shrink-0 overflow-hidden bg-surface-subtle">
          <DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} className="object-cover" />
        </span>
        <span className="min-w-0 self-center">
          <strong className="block text-pretty text-sm leading-5 text-content">{node.canonicalTitle}</strong>
          <span className="mt-1 block text-xs text-content-muted">{nodeMeta(node)}</span>
        </span>
      </span>
    </button>
  );
}

type OverlayNodeProps = {
  node: MediaFranchiseNodeDto;
  onNavigate: (id: string) => void;
  shape?: OverlayShape;
};

function TextOverlayNode({ node, onNavigate, shape }: OverlayNodeProps & { shape: "portrait" | "wide" }) {
  const artworkUrl = shape === "wide" ? node.backgroundUrl ?? node.posterUrl : node.posterUrl;
  return (
    <button
      type="button"
      onClick={() => onNavigate(node.mediaTitleId)}
      className="group block w-full text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <span className={`relative block overflow-hidden bg-surface-subtle ${overlayShapeClasses[shape]}`}>
        <DetailArtwork posterUrl={artworkUrl} title={node.canonicalTitle} className="transition-transform duration-200 ease-out group-hover:scale-[1.025]" />
        <span className="absolute inset-0 bg-linear-to-t from-black/95 via-black/35 via-55% to-transparent" aria-hidden />
        <span className="absolute inset-x-0 bottom-0 block p-3 text-white sm:p-4">
          <strong className="block text-pretty text-sm leading-5 sm:text-base">{node.canonicalTitle}</strong>
          {shape === "portrait" ? <span className="mt-1 block text-xs text-white/80">{nodeMeta(node)}</span> : null}
        </span>
      </span>
    </button>
  );
}

function OverlayNode({ node, onNavigate, shape = "portrait" }: OverlayNodeProps) {
  if (shape === "square") return <SquareOverlayNode node={node} meta={nodeMeta(node)} onNavigate={onNavigate} />;
  if (shape === "wide") return <TextOverlayNode node={node} onNavigate={onNavigate} shape="wide" />;
  return <TextOverlayNode node={node} onNavigate={onNavigate} shape="portrait" />;
}

type OverlayShape = Exclude<FranchiseCardType, "compact">;

const overlayShapeClasses: Record<OverlayShape, string> = {
  portrait: "aspect-3/4",
  square: "aspect-square",
  wide: "aspect-4/3",
};

const overlayGridClasses: Record<OverlayShape, string> = {
  portrait: "grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 xl:grid-cols-6",
  square: "grid-cols-4 sm:grid-cols-5 lg:grid-cols-7 xl:grid-cols-8",
  wide: "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4",
};

const cardGridClasses: Record<FranchiseCardType, string> = {
  compact: "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4",
  ...overlayGridClasses,
};

const timelineColumnClasses: Record<FranchiseCardType, string> = {
  compact: "w-64",
  portrait: "w-48",
  square: "w-32",
  wide: "w-80",
};

const episodeColumnClasses: Record<FranchiseCardType, string> = {
  compact: "lg:grid-cols-[7rem_19rem_1fr]",
  portrait: "lg:grid-cols-[7rem_12rem_1fr]",
  square: "lg:grid-cols-[7rem_8rem_1fr]",
  wide: "lg:grid-cols-[7rem_22rem_1fr]",
};

const posterShapeStorageKey = "cantaro.franchise.poster-shape";
const layoutStorageKey = "cantaro.franchise.layout";

function readCardType(): FranchiseCardType {
  if (typeof window === "undefined") return "compact";
  const value = window.localStorage.getItem(posterShapeStorageKey);
  return value === "portrait" || value === "square" || value === "wide" ? value : "compact";
}

function readLayoutVariant(): FranchiseLayoutId {
  if (typeof window === "undefined") return "release-horizontal";
  const value = window.localStorage.getItem(layoutStorageKey);
  const legacyLayouts: Record<string, FranchiseLayoutId> = {
    "3": "release-horizontal",
    "4": "episode",
    "5": "release-vertical",
    "7": "format",
    "8": "table",
  };
  if (value && legacyLayouts[value]) return legacyLayouts[value];
  return layoutOptions.some((variant) => variant.id === value)
    ? value as FranchiseLayoutId
    : "release-horizontal";
}

function FranchiseCard({ node, onNavigate, cardType, emphasis = false }: {
  node: MediaFranchiseNodeDto;
  onNavigate: (id: string) => void;
  cardType: FranchiseCardType;
  emphasis?: boolean;
}) {
  return cardType === "compact"
    ? <CompactNode node={node} onNavigate={onNavigate} emphasis={emphasis} />
    : <OverlayNode node={node} onNavigate={onNavigate} shape={cardType} />;
}

function branchEntries(presentation: FranchisePresentation) {
  const seen = new Set<string>();
  return presentation.branchGroups.flatMap((group) => group.branches
    .filter((branch) => {
      if (seen.has(branch.node.mediaTitleId)) return false;
      seen.add(branch.node.mediaTitleId);
      return true;
    })
    .map((branch) => ({ source: group.source, branch })));
}

type BranchEntry = ReturnType<typeof branchEntries>[number];

function ReleaseTimeline({ graph, onNavigate, cardType }: LayoutProps) {
  const years = [...new Set(graph.nodes.map((node) => node.startYear ?? 0))].sort((a, b) => a - b);
  const scrollRef = useRef<HTMLDivElement>(null);
  const drag = useRef({ pointerId: -1, startX: 0, scrollLeft: 0, moved: false });

  const beginDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== "mouse" || event.button !== 0) return;
    const element = scrollRef.current;
    if (!element) return;
    drag.current = { pointerId: event.pointerId, startX: event.clientX, scrollLeft: element.scrollLeft, moved: false };
  };
  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const element = scrollRef.current;
    if (!element || drag.current.pointerId !== event.pointerId) return;
    const distance = event.clientX - drag.current.startX;
    if (Math.abs(distance) > 6 && !drag.current.moved) {
      drag.current.moved = true;
      element.setPointerCapture(event.pointerId);
    }
    if (drag.current.moved) element.scrollLeft = drag.current.scrollLeft - distance;
  };
  const endDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (drag.current.pointerId !== event.pointerId) return;
    const element = scrollRef.current;
    if (element?.hasPointerCapture(event.pointerId)) {
      element.releasePointerCapture(event.pointerId);
    }
    drag.current.pointerId = -1;
  };
  return (
    <div
      ref={scrollRef}
      className="cursor-grab touch-auto select-none overflow-x-auto overscroll-x-contain pb-4 active:cursor-grabbing"
      onPointerDown={beginDrag}
      onPointerMove={moveDrag}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onClickCapture={(event) => { if (drag.current.moved) { event.preventDefault(); event.stopPropagation(); drag.current.moved = false; } }}
      aria-label="By release, left to right. Drag horizontally or use the scrollbar to explore."
    >
      <ol className="flex min-w-max items-start gap-0 pt-5">
        {years.map((year) => (
          <li key={year} className={`relative shrink-0 border-t-2 border-border-subtle px-3 pt-6 first:pl-0 last:pr-0 ${timelineColumnClasses[cardType]}`}>
            <span className="absolute -top-2 left-3 h-3.5 w-3.5 rounded-full border-2 border-surface bg-focus" aria-hidden />
            <h2 className="text-xl font-black text-content">{year || "Unknown"}</h2>
            <div className="mt-4 space-y-3">
              {graph.nodes.filter((node) => (node.startYear ?? 0) === year).map((node) => (
                <FranchiseCard key={node.mediaTitleId} node={node} onNavigate={onNavigate} cardType={cardType} />
              ))}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

function GroupedBranches({ entries, onNavigate, cardType = "compact" }: {
  entries: BranchEntry[];
  onNavigate: (id: string) => void;
  cardType?: FranchiseCardType;
}) {
  const groups = new Map<string, BranchEntry[]>();
  for (const entry of entries) {
    const label = relationLabel(entry.branch.displayRelationType);
    groups.set(label, [...(groups.get(label) ?? []), entry]);
  }
  return (
    <div className="space-y-4">
      {[...groups.entries()].map(([label, items]) => (
        <section key={label}>
          <h3 className="mb-2 text-xs font-bold text-content-muted">{label}</h3>
          <div className={`grid gap-3 ${cardType === "compact" ? "sm:grid-cols-2" : cardGridClasses[cardType]}`}>
            {items.map(({ branch }) => <FranchiseCard key={branch.node.mediaTitleId} node={branch.node} onNavigate={onNavigate} cardType={cardType} />)}
          </div>
        </section>
      ))}
    </div>
  );
}

function EpisodeJourney({ graph, presentation, onNavigate, cardType }: LayoutProps) {
  const offsets = graph.continuity.episodeOffsetByMediaTitleId;
  const branches = branchEntries(presentation);
  const continuityIds = new Set(presentation.continuityNodes.map((node) => node.mediaTitleId));
  const attachedBranches = branches.filter(({ source }) => continuityIds.has(source.mediaTitleId));
  const attachedIds = new Set(attachedBranches.map(({ branch }) => branch.node.mediaTitleId));
  const remaining = branches.filter(({ branch }) => !continuityIds.has(branch.node.mediaTitleId) && !attachedIds.has(branch.node.mediaTitleId));
  return (
    <div>
      <ol className="space-y-0">
        {presentation.continuityNodes.map((node, index) => {
          const start = (offsets[node.mediaTitleId] ?? 0) + 1;
          const end = node.episodeCount ? start + node.episodeCount - 1 : null;
          const attached = attachedBranches.filter(({ source }) => source.mediaTitleId === node.mediaTitleId);
          return (
            <li key={node.mediaTitleId} className={`grid items-start gap-4 border-l-2 border-focus pb-8 pl-6 ${episodeColumnClasses[cardType]}`}>
              <div className="relative self-start">
                <span className="absolute -left-[1.96rem] top-1 h-3.5 w-3.5 rounded-full bg-focus ring-4 ring-surface" aria-hidden />
                <span className="text-sm font-black text-content">{end ? `Ep. ${start}–${end}` : `Step ${index + 1}`}</span>
              </div>
              <FranchiseCard node={node} onNavigate={onNavigate} cardType={cardType} emphasis />
              <GroupedBranches entries={attached} onNavigate={onNavigate} cardType={cardType} />
            </li>
          );
        })}
      </ol>
      {remaining.length > 0 ? (
        <section className="border-t border-border-subtle pt-6">
          <h2 className="text-lg font-bold text-content">Related outside the episode lane</h2>
          <div className="mt-4"><GroupedBranches entries={remaining} onNavigate={onNavigate} cardType={cardType} /></div>
        </section>
      ) : null}
    </div>
  );
}

function EraLanes({ graph, onNavigate, cardType }: LayoutProps) {
  const eras = [...new Set(graph.nodes.map((node) => node.startYear ?? 0))].sort((a, b) => a - b);
  return (
    <div>
      {eras.map((year) => {
        const nodes = graph.nodes.filter((node) => (node.startYear ?? 0) === year);
        return (
          <section key={year} className="grid items-start gap-5 border-l-2 border-focus pb-8 pl-6 lg:grid-cols-[7rem_1fr]">
            <h2 className="relative text-xl font-black text-content"><span className="absolute -left-[1.96rem] top-1 h-3.5 w-3.5 rounded-full bg-focus ring-4 ring-surface" aria-hidden />{year || "Unscheduled"}</h2>
            <div className={`grid gap-4 ${cardGridClasses[cardType]}`}>
              {nodes.map((node) => <FranchiseCard key={node.mediaTitleId} node={node} onNavigate={onNavigate} cardType={cardType} />)}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function shelfCategory(node: MediaFranchiseNodeDto) {
  const format = node.mediaFormat?.toLowerCase();
  const categoryByFormat: Record<string, string> = {
    tv: "TV seasons",
    tv_short: "TV seasons",
    movie: "Movies",
    ova: "OVA, ONA and specials",
    ona: "OVA, ONA and specials",
    special: "OVA, ONA and specials",
    music: "OVA, ONA and specials",
    manga: "Source material",
    novel: "Source material",
    one_shot: "Source material",
  };
  return categoryByFormat[format ?? ""] ?? "Other releases";
}

function sortNodesByRelease(nodes: MediaFranchiseNodeDto[]) {
  return nodes.sort((left, right) =>
    (left.startYear ?? 9999) - (right.startYear ?? 9999)
    || left.canonicalTitle.localeCompare(right.canonicalTitle));
}

const franchiseCategories = ["TV seasons", "Movies", "OVA, ONA and specials", "Source material", "Other releases"];

function nodesInCategory(graph: MediaFranchiseGraphDto, category: string) {
  return sortNodesByRelease(graph.nodes.filter((node) => shelfCategory(node) === category));
}

function ShelfCategory({ category, graph, onNavigate, cardType }: Pick<LayoutProps, "graph" | "onNavigate" | "cardType"> & { category: string }) {
  const nodes = nodesInCategory(graph, category);
  if (nodes.length === 0) return null;
  return (
    <section>
      <h2 className="text-xl font-black text-content">{category}</h2>
      <div className={`mt-4 grid gap-4 ${cardGridClasses[cardType]}`}>
        {nodes.map((node) => <FranchiseCard key={node.mediaTitleId} node={node} onNavigate={onNavigate} cardType={cardType} />)}
      </div>
    </section>
  );
}

function FranchiseShelf({ graph, onNavigate, cardType }: LayoutProps) {
  return (
    <div className="space-y-9">
      <div className="flex flex-wrap gap-x-6 gap-y-2 border-y border-border-subtle py-3 text-sm text-content-muted">
        {franchiseCategories.map((category) => <span key={category}><strong className="text-content">{graph.nodes.filter((node) => shelfCategory(node) === category).length}</strong> {category.toLowerCase()}</span>)}
      </div>
      {franchiseCategories.map((category) => <ShelfCategory key={category} category={category} graph={graph} onNavigate={onNavigate} cardType={cardType} />)}
    </div>
  );
}

type FranchiseTableSort = "title" | "year" | "format" | "episodes" | "relation";

function matchesSelection(value: string | number | null | undefined, selection: string) {
  return selection === "all" || String(value) === selection;
}

function matchesMinimum(value: number | null | undefined, minimum: string) {
  return minimum === "" || (value ?? 0) >= Number(minimum);
}

function matchesMaximum(value: number | null | undefined, maximum: string) {
  return maximum === "" || (value ?? Number.POSITIVE_INFINITY) <= Number(maximum);
}

function tableSortValue(node: MediaFranchiseNodeDto, sort: FranchiseTableSort, relationship: string) {
  const values: Record<FranchiseTableSort, string | number> = {
    title: node.canonicalTitle,
    year: node.startYear ?? 9999,
    format: node.mediaFormat ?? node.mediaKind,
    episodes: node.episodeCount ?? -1,
    relation: relationship,
  };
  return values[sort];
}

function compareTableValues(left: string | number, right: string | number) {
  return typeof left === "number" && typeof right === "number"
    ? left - right
    : String(left).localeCompare(String(right));
}

function FranchiseTable({ graph, presentation, onNavigate }: LayoutProps) {
  const branches = branchEntries(presentation);
  const formats = [...new Set(graph.nodes.map((node) => node.mediaFormat ?? node.mediaKind))].sort();
  const years = [...new Set(graph.nodes.map((node) => node.startYear).filter((year): year is number => year != null))].sort((left, right) => left - right);
  const [query, setQuery] = useState("");
  const [format, setFormat] = useState("all");
  const [relation, setRelation] = useState("all");
  const [year, setYear] = useState("all");
  const [minimumEpisodes, setMinimumEpisodes] = useState("");
  const [maximumEpisodes, setMaximumEpisodes] = useState("");
  const [sort, setSort] = useState<FranchiseTableSort>("year");
  const [descending, setDescending] = useState(false);
  const relationFor = (node: MediaFranchiseNodeDto) => {
    const entry = branches.find(({ branch }) => branch.node.mediaTitleId === node.mediaTitleId);
    return entry ? relationLabel(entry.branch.displayRelationType) : presentation.continuityNodes.some((item) => item.mediaTitleId === node.mediaTitleId) ? "Main series" : "Related title";
  };
  const relations = [...new Set(graph.nodes.map(relationFor))].sort();
  const rows = graph.nodes.filter((node) => {
    const matches = [
      node.canonicalTitle.toLowerCase().includes(query.trim().toLowerCase()),
      matchesSelection(node.mediaFormat ?? node.mediaKind, format),
      matchesSelection(relationFor(node), relation),
      matchesSelection(node.startYear, year),
      matchesMinimum(node.episodeCount, minimumEpisodes),
      matchesMaximum(node.episodeCount, maximumEpisodes),
    ];
    return matches.every(Boolean);
  }).sort((left, right) => {
    const result = compareTableValues(
      tableSortValue(left, sort, relationFor(left)),
      tableSortValue(right, sort, relationFor(right)),
    );
    return descending ? -result : result;
  });
  const sortHeader = (key: typeof sort, label: string) => <button type="button" onClick={() => { if (sort === key) setDescending((value) => !value); else { setSort(key); setDescending(false); } }} className="inline-flex items-center gap-1 font-bold text-content hover:text-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus">{label}{sort === key ? descending ? <ArrowDown size={14} /> : <ArrowUp size={14} /> : null}</button>;
  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-3">
        <label className="relative min-w-64 flex-1"><span className="sr-only">Filter titles</span><Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-content-muted" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Filter titles" className="w-full border border-border-subtle bg-surface py-2 pl-9 pr-3 text-sm text-content placeholder:text-content-muted focus:border-focus focus:outline-none" /></label>
        <label><span className="sr-only">Filter by release year</span><select value={year} onChange={(event) => setYear(event.target.value)} className="border border-border-subtle bg-surface px-3 py-2 text-sm text-content focus:border-focus focus:outline-none"><option value="all">All years</option>{years.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
        <label><span className="sr-only">Filter by format</span><select value={format} onChange={(event) => setFormat(event.target.value)} className="border border-border-subtle bg-surface px-3 py-2 text-sm text-content focus:border-focus focus:outline-none"><option value="all">All formats</option>{formats.map((value) => <option key={value} value={value}>{mediaFormatLabel(value)}</option>)}</select></label>
        <label><span className="sr-only">Filter by relationship</span><select value={relation} onChange={(event) => setRelation(event.target.value)} className="border border-border-subtle bg-surface px-3 py-2 text-sm text-content focus:border-focus focus:outline-none"><option value="all">All relationships</option>{relations.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
        <label><span className="sr-only">Minimum episodes</span><input type="number" min="0" value={minimumEpisodes} onChange={(event) => setMinimumEpisodes(event.target.value)} placeholder="Min episodes" className="w-32 border border-border-subtle bg-surface px-3 py-2 text-sm text-content placeholder:text-content-muted focus:border-focus focus:outline-none" /></label>
        <label><span className="sr-only">Maximum episodes</span><input type="number" min="0" value={maximumEpisodes} onChange={(event) => setMaximumEpisodes(event.target.value)} placeholder="Max episodes" className="w-32 border border-border-subtle bg-surface px-3 py-2 text-sm text-content placeholder:text-content-muted focus:border-focus focus:outline-none" /></label>
      </div>
      <p className="mb-3 text-sm text-content-muted">Showing {rows.length} of {graph.nodes.length} titles</p>
      <div className="overflow-x-auto border-y border-border-subtle">
      <table className="w-full min-w-200 border-collapse text-left text-sm">
        <thead className="text-content-muted">
          <tr className="border-b border-border-subtle"><th className="px-3 py-3">{sortHeader("title", "Title")}</th><th className="px-3 py-3">{sortHeader("year", "Release")}</th><th className="px-3 py-3">{sortHeader("format", "Format")}</th><th className="px-3 py-3">{sortHeader("episodes", "Episodes")}</th><th className="px-3 py-3">{sortHeader("relation", "Relationship")}</th></tr>
        </thead>
        <tbody className="divide-y divide-border-subtle">
          {rows.map((node) => <FranchiseTableRow key={node.mediaTitleId} node={node} branches={branches} presentation={presentation} onNavigate={onNavigate} />)}
        </tbody>
      </table>
      </div>
    </div>
  );
}

function FranchiseTableRow({ node, branches, presentation, onNavigate }: {
  node: MediaFranchiseNodeDto;
  branches: BranchEntry[];
  presentation: FranchisePresentation;
  onNavigate: (id: string) => void;
}) {
  const entry = branches.find(({ branch }) => branch.node.mediaTitleId === node.mediaTitleId);
  const continuityIndex = presentation.continuityNodes.findIndex((item) => item.mediaTitleId === node.mediaTitleId);
  const relationship = continuityIndex >= 0
    ? "Main series"
    : entry ? `${relationLabel(entry.branch.displayRelationType)} from ${entry.source.canonicalTitle}` : "Related title";
  return (
    <tr className="hover:bg-surface-subtle">
      <td className="p-3"><button type="button" onClick={() => onNavigate(node.mediaTitleId)} className="flex max-w-xl items-center gap-3 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"><span className="h-14 w-10 shrink-0 overflow-hidden bg-surface-subtle"><DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} className="object-cover" /></span><span><strong className="block text-pretty text-content">{node.canonicalTitle}</strong></span></button></td>
      <td className="p-3 text-content-muted">{node.startYear ?? "—"}</td>
      <td className="p-3 text-content-muted">{node.mediaFormat ? mediaFormatLabel(node.mediaFormat) : mediaKindLabel(node.mediaKind)}</td>
      <td className="p-3 text-content-muted">{node.episodeCount ?? "—"}</td>
      <td className="p-3 text-content-muted">{relationship}</td>
    </tr>
  );
}

interface LayoutProps {
  graph: MediaFranchiseGraphDto;
  presentation: FranchisePresentation;
  onNavigate: (id: string) => void;
  cardType: FranchiseCardType;
}

const franchiseLayouts: Record<FranchiseLayoutId, ComponentType<LayoutProps>> = {
  "release-horizontal": ReleaseTimeline,
  "release-vertical": EraLanes,
  episode: EpisodeJourney,
  format: FranchiseShelf,
  table: FranchiseTable,
};

function FranchiseLayout({ variant, ...props }: LayoutProps & { variant: FranchiseLayoutId }) {
  const Layout = franchiseLayouts[variant];
  return <Layout {...props} />;
}

function FranchiseControls({
  variant,
  cardType,
  onVariantChange,
  onCardTypeChange,
}: {
  variant: FranchiseLayoutId;
  cardType: FranchiseCardType;
  onVariantChange: (variant: FranchiseLayoutId) => void;
  onCardTypeChange: (cardType: FranchiseCardType) => void;
}) {
  return (
    <div className="flex flex-wrap justify-end gap-3 border-b border-border-subtle py-5">
      <label className="grid gap-1 text-xs font-semibold text-content-muted">
        Layout
        <select value={variant} onChange={(event) => onVariantChange(event.target.value as FranchiseLayoutId)} className="min-w-44 border border-border-subtle bg-surface px-3 py-2 text-sm font-semibold text-content focus:border-focus focus:outline-none">
          {layoutOptions.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
        </select>
      </label>
      <label className="grid gap-1 text-xs font-semibold text-content-muted">
        Card type
        <select value={cardType} disabled={variant === "table"} onChange={(event) => onCardTypeChange(event.target.value as FranchiseCardType)} className="min-w-36 border border-border-subtle bg-surface px-3 py-2 text-sm font-semibold text-content focus:border-focus focus:outline-none disabled:cursor-not-allowed disabled:opacity-50">
          <option value="compact">Compact</option>
          <option value="portrait">Portrait</option>
          <option value="square">Square</option>
          <option value="wide">Wide</option>
        </select>
      </label>
    </div>
  );
}

function ExplorerContent({
  graph,
  error,
  presentation,
  variant,
  cardType,
  onNavigateTitle,
  onRetry,
}: {
  graph: MediaFranchiseGraphDto | null;
  error: string | null;
  presentation: FranchisePresentation | null;
  variant: FranchiseLayoutId;
  cardType: FranchiseCardType;
  onNavigateTitle: (id: string) => void;
  onRetry: () => void;
}) {
  if (error) {
    return <div role="alert" className="flex items-center justify-between gap-4 py-10"><p className="text-content-muted">{error}</p><button type="button" onClick={onRetry} className="inline-flex items-center gap-2 border border-border-subtle px-4 py-2 font-semibold text-content"><RotateCcw size={16} /> Retry</button></div>;
  }
  if (!graph || !presentation) {
    return <div className="h-72 animate-pulse bg-surface-subtle" aria-label="Loading franchise layout" />;
  }
  return <FranchiseLayout variant={variant} graph={graph} presentation={presentation} onNavigate={onNavigateTitle} cardType={cardType} />;
}

function ExplorerFooter({ graph }: { graph: MediaFranchiseGraphDto | null }) {
  if (!graph) return null;
  return <footer className="border-t border-border-subtle pt-5 text-sm text-content-muted"><p><strong className="text-content">Reading key:</strong> every title appears once in the selected view.</p><p className="mt-1">Relations from {graph.sourceProvider === "anilist" ? "AniList" : graph.sourceProvider}{graph.continuity.isComplete ? "" : " · Continuity may be incomplete"}</p></footer>;
}

export function FranchiseExplorer({
  state,
  onRetry,
  onNavigateTitle,
}: {
  state: FranchiseGraphState;
  onRetry: () => void;
  onNavigateTitle?: (mediaTitleId: string) => void;
}) {
  const [variant, setVariant] = useState<FranchiseLayoutId>(readLayoutVariant);
  const [cardType, setCardType] = useState<FranchiseCardType>(readCardType);
  const graph = state.status === "loaded" ? state.value : null;
  const error = state.status === "error" ? state.error : null;
  const presentation = useMemo(() => graph ? buildFranchisePresentation(graph) : null, [graph]);

  useEffect(() => window.localStorage.setItem(layoutStorageKey, String(variant)), [variant]);
  useEffect(() => window.localStorage.setItem(posterShapeStorageKey, cardType), [cardType]);

  return (
    <section aria-label="Franchise">
      <FranchiseControls
        variant={variant}
        cardType={cardType}
        onVariantChange={setVariant}
        onCardTypeChange={setCardType}
      />
      <div className="py-8">
        <ExplorerContent
          graph={graph}
          error={error}
          presentation={presentation}
          variant={variant}
          cardType={cardType}
          onNavigateTitle={onNavigateTitle ?? (() => undefined)}
          onRetry={onRetry}
        />
      </div>
      <ExplorerFooter graph={graph} />
    </section>
  );
}
