import { ArrowDown, ArrowLeft, ArrowUp, Check, GitBranch, RotateCcw, Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState, type ComponentType, type PointerEvent as ReactPointerEvent } from "react";
import { DetailArtwork } from "../components/media-entry-detail/EntryDisplayPrimitives";
import { mediaApi, type MediaFranchiseGraphDto, type MediaFranchiseNodeDto } from "../services/mediaApi";
import { mediaFormatLabel, mediaKindLabel } from "../services/mediaFormatting";
import {
  buildFranchisePresentation,
  relationLabel,
  type FranchisePresentation,
} from "./media-entry-detail/franchiseGraph";

export type FranchiseDesignVariant = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

interface FranchiseDesignLabPageProps {
  mediaTitleId: string;
  variant: FranchiseDesignVariant;
  onBack: () => void;
  onNavigateTitle: (mediaTitleId: string) => void;
  onNavigateVariant: (variant: FranchiseDesignVariant) => void;
}

const variantNames: Record<FranchiseDesignVariant, string> = {
  1: "Directed levels",
  2: "Current-title focus",
  3: "Release timeline",
  4: "Episode journey",
  5: "Release timeline · vertical",
  6: "Franchise map",
  7: "By format",
  8: "Franchise table",
};

const visibleVariants: Array<{ id: FranchiseDesignVariant; label: string }> = [
  { id: 3, label: "By release →" },
  { id: 4, label: "By episode" },
  { id: 5, label: "By release ↓" },
  { id: 7, label: "By format" },
  { id: 8, label: "Table" },
];

function nodeMeta(node: MediaFranchiseNodeDto) {
  return [
    node.mediaFormat ? mediaFormatLabel(node.mediaFormat) : mediaKindLabel(node.mediaKind),
    node.episodeCount ? `${node.episodeCount} ep.` : null,
    node.startYear,
  ].filter(Boolean).join(" · ");
}

function NodeContext({ context }: { context?: string }) {
  return context
    ? <span className="mb-2 block text-xs font-semibold text-content-muted">{context}</span>
    : null;
}

function CurrentMarker({ isCurrent }: { isCurrent: boolean }) {
  return isCurrent
    ? <span className="mt-2 inline-flex items-center gap-1 text-xs font-semibold text-focus"><Check size={12} /> Current title</span>
    : null;
}

function CompactNode({
  node,
  onNavigate,
  emphasis = false,
  context,
}: {
  node: MediaFranchiseNodeDto;
  onNavigate: (id: string) => void;
  emphasis?: boolean;
  context?: string;
}) {
  return (
    <button
      type="button"
      onClick={() => onNavigate(node.mediaTitleId)}
      className="group min-w-0 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      aria-current={node.isCurrent ? "page" : undefined}
    >
      <NodeContext context={context} />
      <span className={`flex min-w-0 gap-3 border bg-surface p-3 transition-colors group-hover:border-content-subtle ${emphasis || node.isCurrent ? "border-focus" : "border-border-subtle"}`}>
        <span className="relative block h-24 w-16 shrink-0 overflow-hidden bg-surface-subtle">
          <DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} className="object-cover" />
        </span>
        <span className="min-w-0 self-center">
          <strong className="block text-pretty text-sm leading-5 text-content">{node.canonicalTitle}</strong>
          <span className="mt-1 block text-xs text-content-muted">{nodeMeta(node)}</span>
          <CurrentMarker isCurrent={node.isCurrent} />
        </span>
      </span>
    </button>
  );
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

function BranchNodeList({
  entries,
  onNavigate,
}: {
  entries: BranchEntry[];
  onNavigate: (id: string) => void;
}) {
  return entries.map(({ source, branch }) => (
    <CompactNode
      key={branch.node.mediaTitleId}
      node={branch.node}
      onNavigate={onNavigate}
      context={`${relationLabel(branch.displayRelationType)} from ${source.canonicalTitle}`}
    />
  ));
}

function DirectedLevels({ presentation, onNavigate }: LayoutProps) {
  const branches = branchEntries(presentation);
  return (
    <div className="overflow-x-auto pb-4">
      <div className="min-w-240">
        <div className="grid grid-cols-[14rem_1fr_16rem] gap-8 text-sm font-bold text-content-muted">
          <span>Origins</span><span>Main continuity · earlier to later</span><span>Related branches</span>
        </div>
        <div className="mt-4 grid grid-cols-[14rem_1fr_16rem] items-start gap-8">
          <div className="space-y-4">
            <BranchNodeList entries={branches.filter(({ branch }) => ["source", "adaptation"].includes(branch.displayRelationType)).slice(0, 3)} onNavigate={onNavigate} />
          </div>
          <ol className="space-y-3 border-l border-border-subtle pl-6">
            {presentation.continuityNodes.map((node, index) => (
              <li key={node.mediaTitleId} className="relative grid grid-cols-[2rem_1fr] items-center gap-3">
                <span className="absolute -left-[1.68rem] h-px w-6 bg-border-subtle" aria-hidden />
                <span className="text-sm font-black text-content-subtle">{index + 1}</span>
                <CompactNode node={node} onNavigate={onNavigate} emphasis />
              </li>
            ))}
          </ol>
          <div className="space-y-4">
            <BranchNodeList entries={branches.filter(({ branch }) => !["source", "adaptation"].includes(branch.displayRelationType)).slice(0, 5)} onNavigate={onNavigate} />
          </div>
        </div>
      </div>
    </div>
  );
}

function CurrentFocus({ presentation, onNavigate }: LayoutProps) {
  const currentIndex = Math.max(0, presentation.continuityNodes.findIndex((node) => node.isCurrent));
  const before = presentation.continuityNodes.slice(0, currentIndex);
  const current = presentation.continuityNodes[currentIndex];
  const after = presentation.continuityNodes.slice(currentIndex + 1);
  const branches = branchEntries(presentation);
  return (
    <div>
      <div className="grid items-center gap-5 lg:grid-cols-[1fr_auto_1fr]">
        <div className="space-y-3">
          <p className="text-sm font-bold text-content-muted">Leads here from</p>
          {before.slice(-2).map((node) => <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} />)}
        </div>
        <div className="mx-auto w-full max-w-80">
          <p className="mb-3 text-center text-sm font-bold text-focus">You are here</p>
          {current ? <CompactNode node={current} onNavigate={onNavigate} emphasis /> : null}
        </div>
        <div className="space-y-3">
          <p className="text-sm font-bold text-content-muted">Continues as</p>
          {after.slice(0, 2).map((node) => <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} />)}
        </div>
      </div>
      <div className="mt-10 border-t border-border-subtle pt-6">
        <h2 className="text-lg font-bold text-content">Directly connected to this path</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          <BranchNodeList entries={branches.slice(0, 9)} onNavigate={onNavigate} />
        </div>
      </div>
    </div>
  );
}

function ReleaseTimeline({ graph, onNavigate }: LayoutProps) {
  const years = [...new Set(graph.nodes.map((node) => node.startYear ?? 0))].sort((a, b) => a - b);
  const scrollRef = useRef<HTMLDivElement>(null);
  const drag = useRef({ pointerId: -1, startX: 0, scrollLeft: 0, moved: false });

  const beginDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.pointerType === "mouse" && event.button !== 0) return;
    const element = scrollRef.current;
    if (!element) return;
    drag.current = { pointerId: event.pointerId, startX: event.clientX, scrollLeft: element.scrollLeft, moved: false };
    element.setPointerCapture(event.pointerId);
  };
  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const element = scrollRef.current;
    if (!element || drag.current.pointerId !== event.pointerId) return;
    const distance = event.clientX - drag.current.startX;
    if (Math.abs(distance) > 6) drag.current.moved = true;
    if (drag.current.moved) element.scrollLeft = drag.current.scrollLeft - distance;
  };
  const endDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (drag.current.pointerId !== event.pointerId) return;
    scrollRef.current?.releasePointerCapture(event.pointerId);
    drag.current.pointerId = -1;
  };
  return (
    <div
      ref={scrollRef}
      className="cursor-grab touch-pan-y select-none overflow-x-auto pb-4 active:cursor-grabbing"
      onPointerDown={beginDrag}
      onPointerMove={moveDrag}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onClickCapture={(event) => { if (drag.current.moved) { event.preventDefault(); event.stopPropagation(); drag.current.moved = false; } }}
      aria-label="Release timeline. Drag horizontally or use the scrollbar to explore."
    >
      <ol className="flex min-w-max items-start gap-0 pt-5">
        {years.map((year) => (
          <li key={year} className="relative w-64 border-t-2 border-border-subtle px-3 pt-6 first:pl-0 last:pr-0">
            <span className="absolute -top-2 left-3 h-3.5 w-3.5 rounded-full border-2 border-surface bg-focus" aria-hidden />
            <h2 className="text-xl font-black text-content">{year || "Unknown"}</h2>
            <div className="mt-4 space-y-3">
              {graph.nodes.filter((node) => (node.startYear ?? 0) === year).map((node) => (
                <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} />
              ))}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

function GroupedBranches({ entries, onNavigate }: {
  entries: BranchEntry[];
  onNavigate: (id: string) => void;
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
          <div className="grid gap-3 sm:grid-cols-2">
            {items.map(({ branch }) => <CompactNode key={branch.node.mediaTitleId} node={branch.node} onNavigate={onNavigate} />)}
          </div>
        </section>
      ))}
    </div>
  );
}

function EpisodeJourney({ graph, presentation, onNavigate }: LayoutProps) {
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
            <li key={node.mediaTitleId} className="grid items-start gap-4 border-l-2 border-focus pb-8 pl-6 lg:grid-cols-[7rem_19rem_1fr]">
              <div className="relative self-start">
                <span className="absolute -left-[1.96rem] top-1 h-3.5 w-3.5 rounded-full bg-focus ring-4 ring-surface" aria-hidden />
                <span className="text-sm font-black text-content">{end ? `Ep. ${start}–${end}` : `Step ${index + 1}`}</span>
              </div>
              <CompactNode node={node} onNavigate={onNavigate} emphasis />
              <GroupedBranches entries={attached} onNavigate={onNavigate} />
            </li>
          );
        })}
      </ol>
      {remaining.length > 0 ? (
        <section className="border-t border-border-subtle pt-6">
          <h2 className="text-lg font-bold text-content">Related outside the episode lane</h2>
          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3"><BranchNodeList entries={remaining} onNavigate={onNavigate} /></div>
        </section>
      ) : null}
    </div>
  );
}

function EraLanes({ graph, presentation, onNavigate }: LayoutProps) {
  const branches = branchEntries(presentation);
  const eras = [...new Set(graph.nodes.map((node) => node.startYear ?? 0))].sort((a, b) => a - b);
  return (
    <div>
      {eras.map((year) => {
        const nodes = graph.nodes.filter((node) => (node.startYear ?? 0) === year);
        return (
          <section key={year} className="grid items-start gap-5 border-l-2 border-focus pb-8 pl-6 lg:grid-cols-[7rem_1fr]">
            <h2 className="relative text-xl font-black text-content"><span className="absolute -left-[1.96rem] top-1 h-3.5 w-3.5 rounded-full bg-focus ring-4 ring-surface" aria-hidden />{year || "Unscheduled"}</h2>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              {nodes.map((node) => {
                const relation = branches.find(({ branch }) => branch.node.mediaTitleId === node.mediaTitleId);
                const isContinuity = presentation.continuityNodes.some((item) => item.mediaTitleId === node.mediaTitleId);
                return <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} emphasis={isContinuity} context={isContinuity ? "Main continuity" : relation ? `${relationLabel(relation.branch.displayRelationType)} from ${relation.source.canonicalTitle}` : "Related title"} />;
              })}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function FranchiseMap({ graph, presentation, onNavigate }: LayoutProps) {
  const continuityIds = new Set(presentation.continuityNodes.map((node) => node.mediaTitleId));
  const entries = branchEntries(presentation).filter(({ branch }) => !continuityIds.has(branch.node.mediaTitleId));
  const attachedEntries = entries.filter(({ source }) => continuityIds.has(source.mediaTitleId));
  const represented = new Set([...continuityIds, ...attachedEntries.map(({ branch }) => branch.node.mediaTitleId)]);
  const fallbackSource = presentation.continuityNodes[0] ?? graph.nodes[0];
  const remaining = graph.nodes.filter((node) => !represented.has(node.mediaTitleId));
  return (
    <div className="mx-auto max-w-5xl">
      <ol className="space-y-0">
        {presentation.continuityNodes.map((node, index) => {
          const attached = attachedEntries.filter(({ source }) => source.mediaTitleId === node.mediaTitleId);
          return (
            <li key={node.mediaTitleId} className="relative border-l-2 border-focus pb-9 pl-7">
              <span className="absolute -left-3 top-0 grid h-6 w-6 place-items-center rounded-full bg-focus text-xs font-black text-white">{index + 1}</span>
              <div className="grid gap-5 lg:grid-cols-[20rem_1fr]">
                <CompactNode node={node} onNavigate={onNavigate} emphasis />
                <GroupedBranches entries={attached} onNavigate={onNavigate} />
              </div>
            </li>
          );
        })}
      </ol>
      {remaining.length > 0 && fallbackSource ? (
        <section className="border-t border-border-subtle pt-6">
          <h2 className="text-lg font-bold text-content">Other related titles</h2>
          <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {remaining.map((node) => <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} context={`Related to ${fallbackSource.canonicalTitle}`} />)}
          </div>
        </section>
      ) : null}
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

function FranchiseShelf({ graph, presentation, onNavigate }: LayoutProps) {
  const categories = ["TV seasons", "Movies", "OVA, ONA and specials", "Source material", "Other releases"];
  return (
    <div className="space-y-9">
      <div className="flex flex-wrap gap-x-6 gap-y-2 border-y border-border-subtle py-3 text-sm text-content-muted">
        {categories.map((category) => <span key={category}><strong className="text-content">{graph.nodes.filter((node) => shelfCategory(node) === category).length}</strong> {category.toLowerCase()}</span>)}
      </div>
      {categories.map((category) => {
        const nodes = graph.nodes.filter((node) => shelfCategory(node) === category).sort((left, right) =>
          (left.startYear ?? 9999) - (right.startYear ?? 9999)
          || left.canonicalTitle.localeCompare(right.canonicalTitle));
        if (nodes.length === 0) return null;
        return (
          <section key={category}>
            <h2 className="text-xl font-black text-content">{category}</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {nodes.map((node) => {
                const continuityIndex = presentation.continuityNodes.findIndex((item) => item.mediaTitleId === node.mediaTitleId);
                return <CompactNode key={node.mediaTitleId} node={node} onNavigate={onNavigate} emphasis={continuityIndex >= 0} />;
              })}
            </div>
          </section>
        );
      })}
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
    return entry ? relationLabel(entry.branch.displayRelationType) : presentation.continuityNodes.some((item) => item.mediaTitleId === node.mediaTitleId) ? "Main continuity" : "Related title";
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
    ? `Continuity ${continuityIndex + 1}`
    : entry ? `${relationLabel(entry.branch.displayRelationType)} from ${entry.source.canonicalTitle}` : "Related title";
  return (
    <tr className="hover:bg-surface-subtle">
      <td className="p-3"><button type="button" onClick={() => onNavigate(node.mediaTitleId)} className="flex max-w-xl items-center gap-3 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"><span className="h-14 w-10 shrink-0 overflow-hidden bg-surface-subtle"><DetailArtwork posterUrl={node.posterUrl} title={node.canonicalTitle} className="object-cover" /></span><span><strong className="block text-pretty text-content">{node.canonicalTitle}</strong><CurrentMarker isCurrent={node.isCurrent} /></span></button></td>
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
}

const designLayouts: Record<FranchiseDesignVariant, ComponentType<LayoutProps>> = {
  1: DirectedLevels,
  2: CurrentFocus,
  3: ReleaseTimeline,
  4: EpisodeJourney,
  5: EraLanes,
  6: FranchiseMap,
  7: FranchiseShelf,
  8: FranchiseTable,
};

function DesignLayout({ variant, ...props }: LayoutProps & { variant: FranchiseDesignVariant }) {
  const Layout = designLayouts[variant];
  return <Layout {...props} />;
}

function useFranchiseDesignGraph(mediaTitleId: string) {
  const [graph, setGraph] = useState<MediaFranchiseGraphDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setGraph(null);
    setError(null);
    mediaApi.getFranchiseGraph(mediaTitleId).then((value) => {
      if (!cancelled) setGraph(value);
    }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : "Couldn’t load franchise connections.");
    });
    return () => { cancelled = true; };
  }, [mediaTitleId, reloadKey]);

  const presentation = useMemo(() => graph ? buildFranchisePresentation(graph) : null, [graph]);
  return { graph, error, presentation, reload: () => setReloadKey((value) => value + 1) };
}

function LabHeader({
  graph,
  variant,
  onNavigateVariant,
}: {
  graph: MediaFranchiseGraphDto | null;
  variant: FranchiseDesignVariant;
  onNavigateVariant: (variant: FranchiseDesignVariant) => void;
}) {
  const current = graph?.nodes.find((node) => node.isCurrent);
  return (
    <header className="mt-5 flex flex-wrap items-end justify-between gap-5 border-b border-border-subtle pb-6">
      <div className="max-w-3xl">
        <div className="flex items-center gap-2 text-sm font-semibold text-content-muted"><GitBranch size={16} /> Franchise layout study</div>
        <h1 className="mt-2 text-3xl font-black text-content">{variantNames[variant]}</h1>
        <p className="mt-2 text-pretty text-content-muted">{current?.canonicalTitle ?? "Franchise"} · Explore how chronology, direction, and related works could fit together.</p>
      </div>
      <nav aria-label="Franchise layout variants" className="flex flex-wrap gap-2">
        {visibleVariants.map((item) => <button key={item.id} type="button" onClick={() => onNavigateVariant(item.id)} aria-current={item.id === variant ? "page" : undefined} className={`border px-3 py-2 text-sm font-bold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${item.id === variant ? "border-focus bg-focus text-white" : "border-border-subtle text-content-muted hover:border-content-subtle hover:text-content"}`}>{item.label}</button>)}
      </nav>
    </header>
  );
}

function LabContent({
  graph,
  error,
  presentation,
  variant,
  onNavigateTitle,
  onRetry,
}: {
  graph: MediaFranchiseGraphDto | null;
  error: string | null;
  presentation: FranchisePresentation | null;
  variant: FranchiseDesignVariant;
  onNavigateTitle: (id: string) => void;
  onRetry: () => void;
}) {
  if (error) {
    return <div role="alert" className="flex items-center justify-between gap-4 py-10"><p className="text-content-muted">{error}</p><button type="button" onClick={onRetry} className="inline-flex items-center gap-2 border border-border-subtle px-4 py-2 font-semibold text-content"><RotateCcw size={16} /> Retry</button></div>;
  }
  if (!graph || !presentation) {
    return <div className="h-72 animate-pulse bg-surface-subtle" aria-label="Loading franchise layout" />;
  }
  return <DesignLayout variant={variant} graph={graph} presentation={presentation} onNavigate={onNavigateTitle} />;
}

function LabFooter({ graph }: { graph: MediaFranchiseGraphDto | null }) {
  if (!graph) return null;
  return <footer className="border-t border-border-subtle pt-5 text-sm text-content-muted"><p><strong className="text-content">Reading key:</strong> every title appears once in the selected view; current context is marked without changing franchise membership.</p><p className="mt-1">Relations from {graph.sourceProvider === "anilist" ? "AniList" : graph.sourceProvider}{graph.continuity.isComplete ? "" : " · Continuity may be incomplete"}</p></footer>;
}

export function FranchiseDesignLabPage({ mediaTitleId, variant, onBack, onNavigateTitle, onNavigateVariant }: FranchiseDesignLabPageProps) {
  const { graph, error, presentation, reload } = useFranchiseDesignGraph(mediaTitleId);

  return (
    <div className="mx-auto w-full max-w-360 px-4 py-6 sm:px-7 xl:px-9">
      <button type="button" onClick={onBack} className="inline-flex items-center gap-2 text-sm font-semibold text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"><ArrowLeft size={17} /> Back to title</button>
      <LabHeader graph={graph} variant={variant} onNavigateVariant={onNavigateVariant} />
      <div className="py-8"><LabContent graph={graph} error={error} presentation={presentation} variant={variant} onNavigateTitle={onNavigateTitle} onRetry={reload} /></div>
      <LabFooter graph={graph} />
    </div>
  );
}
