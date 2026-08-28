import { Check, ChevronRight, RotateCcw } from "lucide-react";
import { ActionButton } from "../../../ui";
import { DetailArtwork } from "../../components/media-entry-detail/EntryDisplayPrimitives";
import {
  mediaFormatLabel,
  mediaKindLabel,
} from "../../services/mediaFormatting";
import type {
  MediaFranchiseGraphDto,
  MediaFranchiseNodeDto,
} from "../../services/mediaApi";
import { DetailSectionHeading } from "./MediaDetailSections";
import type { FranchiseGraphState } from "./mediaEntryDetailTypes";
import {
  buildFranchisePresentation,
  continuityRelationLabel,
} from "./franchiseGraph";

interface FranchiseSectionProps {
  state: FranchiseGraphState;
  onRetry: () => void;
  onViewAll?: () => void;
  onNavigateTitle?: (mediaTitleId: string) => void;
}

type FranchiseNavigation = Pick<FranchiseSectionProps, "onNavigateTitle">;

function libraryStatusLabel(status?: string): string | null {
  if (!status) return null;
  const labels: Record<string, string> = {
    completed: "Completed",
    current: "Watching",
    dropped: "Dropped",
    paused: "Paused",
    planned: "Planning",
    repeating: "Rewatching",
  };
  return labels[status.toLowerCase()] ?? status;
}

function nodeMetadata(node: MediaFranchiseNodeDto): string {
  return [
    node.mediaFormat
      ? mediaFormatLabel(node.mediaFormat)
      : mediaKindLabel(node.mediaKind),
    node.episodeCount ? `${node.episodeCount} episodes` : null,
    node.startYear ? String(node.startYear) : null,
  ]
    .filter(Boolean)
    .join(" · ");
}

function NodeState({ node }: { node: MediaFranchiseNodeDto }) {
  if (!node.isInLibrary) {
    return (
      <span className="block truncate text-xs text-white/65">
        Not in library
      </span>
    );
  }
  const status = libraryStatusLabel(node.viewerStatus) ?? "In your library";
  const progress =
    node.progressEpisodes != null
      ? `${node.progressEpisodes}${node.episodeCount ? `/${node.episodeCount}` : ""} episodes`
      : null;
  return (
    <span className="block truncate text-xs text-white">
      <span className="inline-flex items-center gap-1 font-semibold">
        <Check size={13} aria-hidden /> {status}
      </span>
      {progress ? (
        <span className="ml-2 text-white/70">{progress}</span>
      ) : null}
    </span>
  );
}

function NodeCardContent({ node }: { node: MediaFranchiseNodeDto }) {
  return (
    <span className="relative block aspect-[3/4] overflow-hidden bg-surface-subtle">
      <DetailArtwork
        posterUrl={node.posterUrl}
        title={node.canonicalTitle}
        className="object-cover transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 motion-reduce:transition-none"
      />
      <span className="absolute inset-x-0 bottom-0 block min-w-0 bg-linear-to-t from-black/95 via-black/65 to-transparent px-3 pb-3 pt-16 text-left text-white">
        <strong
          className="block text-pretty leading-5 group-hover:text-[#ffd679]"
          title={node.canonicalTitle}
        >
          {node.canonicalTitle}
        </strong>
        <span className="mt-1 block truncate text-xs text-white/70">
          {nodeMetadata(node)}
        </span>
        <span className="mt-1 block">
          <NodeState node={node} />
        </span>
      </span>
    </span>
  );
}

type NodeAction =
  | {
      kind: "title";
      mediaTitleId: string;
      navigate: (mediaTitleId: string) => void;
    }
  | { kind: "external"; url: string }
  | { kind: "static" };

function getNodeAction(
  node: MediaFranchiseNodeDto,
  navigation: FranchiseNavigation,
): NodeAction {
  if (node.isCurrent) return { kind: "static" };
  if (navigation.onNavigateTitle) {
    return {
      kind: "title",
      mediaTitleId: node.mediaTitleId,
      navigate: navigation.onNavigateTitle,
    };
  }
  if (node.externalUrl) return { kind: "external", url: node.externalUrl };
  return { kind: "static" };
}

function NodeCard({
  node,
  fill = false,
  onNavigateTitle,
}: {
  node: MediaFranchiseNodeDto;
  fill?: boolean;
} & FranchiseNavigation) {
  const content = <NodeCardContent node={node} />;
  const className = `group block min-w-0 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${fill ? "w-full" : "w-44 sm:w-48"} ${node.isCurrent ? "cursor-default" : ""}`;
  const action = getNodeAction(node, { onNavigateTitle });
  if (action.kind === "title") {
    return (
      <button
        type="button"
        className={className}
        onClick={() => action.navigate(action.mediaTitleId)}
      >
        {content}
      </button>
    );
  }
  if (action.kind === "external") {
    return (
      <a
        className={className}
        href={action.url}
        target="_blank"
        rel="noreferrer"
      >
        {content}
      </a>
    );
  }
  return (
    <article
      className={className}
      aria-current={node.isCurrent ? "page" : undefined}
    >
      {content}
    </article>
  );
}

function LoadingFranchise() {
  return (
    <div
      className="mt-5 flex gap-4 overflow-hidden"
      aria-busy="true"
      aria-label="Loading franchise connections"
    >
      {[0, 1, 2].map((index) => (
        <span
          key={index}
          className="block aspect-[3/4] w-44 shrink-0 animate-pulse bg-surface-subtle"
        />
      ))}
    </div>
  );
}

function ContinuityLane({
  graph,
  nodes,
  navigation,
}: {
  graph: MediaFranchiseGraphDto;
  nodes: MediaFranchiseNodeDto[];
  navigation: FranchiseNavigation;
}) {
  return (
    <ol
      className="mt-5 flex items-start gap-3 overflow-x-auto pb-3"
      aria-label="Episode continuity order"
    >
      {nodes.map((node, index) => (
        <li key={node.mediaTitleId} className="flex shrink-0 items-start gap-3">
          {index > 0 ? (
            <span className="mt-28 inline-flex w-12 shrink-0 flex-col items-center gap-1 text-center text-[0.65rem] text-content-subtle">
              <ChevronRight size={18} aria-hidden />
              <span>
                {continuityRelationLabel(
                  graph,
                  nodes[index - 1]!.mediaTitleId,
                  node.mediaTitleId,
                )}
              </span>
            </span>
          ) : null}
          <NodeCard node={node} {...navigation} />
        </li>
      ))}
    </ol>
  );
}

function FranchiseHeading({ onViewAll }: { onViewAll?: () => void }) {
  return (
    <DetailSectionHeading
      title="Franchise order"
      detail="Direct episode continuity, followed by related stories."
      action={onViewAll ? "View full graph" : undefined}
      onAction={onViewAll}
    />
  );
}

function LoadingSection() {
  return (
    <section className="py-9">
      <FranchiseHeading />
      <LoadingFranchise />
    </section>
  );
}

function ErrorSection({ onRetry }: { onRetry: () => void }) {
  return (
    <section className="py-9">
      <FranchiseHeading />
      <div
        className="mt-5 flex flex-wrap items-center justify-between gap-4 py-5"
        role="alert"
      >
        <p className="text-content-muted">
          Couldn’t load franchise connections from AniList.
        </p>
        <ActionButton tone="secondary" onClick={onRetry}>
          <RotateCcw size={17} aria-hidden /> Retry
        </ActionButton>
      </div>
    </section>
  );
}

function FranchiseRelations({
  graph,
  presentation,
  navigation,
}: {
  graph: MediaFranchiseGraphDto;
  presentation: ReturnType<typeof buildFranchisePresentation>;
  navigation: FranchiseNavigation;
}) {
  const hasRelations = graph.relations.length > 0 || graph.nodes.length > 1;
  if (!hasRelations) {
    return (
      <p className="mt-5 py-4 text-content-muted">
        AniList lists no prequels, sequels, or related titles for this entry.
      </p>
    );
  }
  return (
    <ContinuityLane
      graph={graph}
      nodes={presentation.continuityNodes}
      navigation={navigation}
    />
  );
}

function FranchisePreviewSummary({
  relatedTitleCount,
}: {
  relatedTitleCount: number;
}) {
  if (relatedTitleCount === 0) return null;
  return (
    <p className="mt-3 text-sm text-content-muted">
      {relatedTitleCount} related {relatedTitleCount === 1 ? "title" : "titles"}{" "}
      in the full graph.
    </p>
  );
}

function LoadedSection({
  graph,
  onViewAll,
  navigation,
}: {
  graph: MediaFranchiseGraphDto;
  onViewAll?: () => void;
  navigation: FranchiseNavigation;
}) {
  const presentation = buildFranchisePresentation(graph);
  const viewAllAction = presentation.relatedTitleCount > 0 ? onViewAll : undefined;
  const providerName =
    graph.sourceProvider === "anilist" ? "AniList" : graph.sourceProvider;

  return (
    <section className="py-9">
      <FranchiseHeading onViewAll={viewAllAction} />
      <FranchiseRelations
        graph={graph}
        presentation={presentation}
        navigation={navigation}
      />
      <FranchisePreviewSummary
        relatedTitleCount={presentation.relatedTitleCount}
      />
      <p className="mt-5 text-xs text-content-subtle">
        Relations from {providerName}
        {graph.continuity.isComplete ? "" : " · Continuity may be incomplete"}
      </p>
    </section>
  );
}

export function FranchiseSection(props: FranchiseSectionProps) {
  if (props.state.status === "loading") return <LoadingSection />;
  if (props.state.status === "error")
    return <ErrorSection onRetry={props.onRetry} />;
  return (
    <LoadedSection
      graph={props.state.value}
      onViewAll={props.onViewAll}
      navigation={{ onNavigateTitle: props.onNavigateTitle }}
    />
  );
}
