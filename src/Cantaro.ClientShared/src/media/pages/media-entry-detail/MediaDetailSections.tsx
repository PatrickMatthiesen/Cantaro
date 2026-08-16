import { useState, type ReactNode } from "react";
import { ExternalLink, Link2, Plus, Unlink } from "lucide-react";
import { ActionButton, IconButton } from "../../../ui";
import { DetailArtwork } from "../../components/media-entry-detail/EntryDisplayPrimitives";
import {
  providerAvailabilityKey,
  type ProviderAvailabilityMap,
  type ProviderAvailabilityState,
} from "../../components/media-entry-detail/providerAvailability";
import { MediaProviderIcon } from "../../components/MediaProviderIcon";
import { StreamingServiceIcon } from "../../components/StreamingServiceIcon";
import { mediaKindLabel } from "../../services/mediaFormatting";
import { mediaProviderCatalog } from "../../services/mediaProviders";
import type {
  MediaEntryDetailModel,
  MediaProviderCharacterCreditDto,
  MediaProviderLinkSummaryDto,
} from "../../services/mediaApi";
import type { StreamingDestination } from "../../services/streamingDestinations";
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from "../../services/streamingServices";
import {
  getPrimaryProgressSummary,
  getRemainingLabel,
  progressKindLabel,
  releaseStatusLabel,
} from "./mediaEntryDetailModel";
import type { ProgressSummary } from "./mediaEntryDetailTypes";

function providerLabel(providerId: string) {
  return (
    mediaProviderCatalog.find((provider) => provider.id === providerId)?.name ??
    providerId
  );
}

export function DetailSectionHeading({
  title,
  detail,
  action,
  onAction,
}: {
  title: string;
  detail?: string;
  action?: string;
  onAction?: () => void;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 className="text-xl font-bold text-content">{title}</h2>
        {detail ? (
          <p className="mt-1 text-sm text-content-muted">{detail}</p>
        ) : null}
      </div>
      {action && onAction ? (
        <button
          type="button"
          onClick={onAction}
          className="min-h-11 text-sm font-semibold text-content-muted transition-colors hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
        >
          {action}
        </button>
      ) : null}
    </div>
  );
}

function availabilityText(availability?: ProviderAvailabilityState) {
  if (!availability || availability.status === "loading")
    return "Checking availability";
  if (availability.status === "error") return "Availability unavailable";
  return availability.links.length > 0
    ? `${availability.links.length} destination${availability.links.length === 1 ? "" : "s"}`
    : "Identity linked";
}

function ProviderRow({
  link,
  availability,
  canManageLinks,
  isUnlinking,
  onUnlink,
}: {
  link: MediaProviderLinkSummaryDto;
  availability?: ProviderAvailabilityState;
  canManageLinks: boolean;
  isUnlinking: boolean;
  onUnlink: (providerId: string) => void;
}) {
  const catalog = mediaProviderCatalog.find(
    (provider) => provider.id === link.provider,
  );
  const content = (
    <>
      <span className="inline-flex size-11 shrink-0 items-center justify-center bg-surface-subtle text-content">
        {catalog ? (
          <MediaProviderIcon providerId={catalog.iconId} aria-hidden />
        ) : (
          link.provider.slice(0, 1).toUpperCase()
        )}
      </span>
      <span className="min-w-0 flex-1">
        <strong className="block truncate text-content">
          {providerLabel(link.provider)}
        </strong>
        <span className="mt-0.5 block truncate text-sm text-content-muted">
          {availabilityText(availability)}
        </span>
      </span>
      {link.externalUrl ? (
        <ExternalLink
          size={17}
          className="shrink-0 text-content-subtle"
          aria-hidden
        />
      ) : null}
    </>
  );

  return (
    <li className="flex min-w-0 items-center gap-3 border-t border-border-subtle py-4 first:border-t-0">
      {link.externalUrl ? (
        <a
          href={link.externalUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="flex min-w-0 flex-1 items-center gap-3 transition-colors hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
        >
          {content}
        </a>
      ) : (
        <div className="flex min-w-0 flex-1 items-center gap-3">{content}</div>
      )}
      {canManageLinks ? (
        <IconButton
          label={`Unlink ${providerLabel(link.provider)}`}
          onClick={() => onUnlink(link.provider)}
          disabled={isUnlinking}
          className="hover:bg-danger-surface hover:text-danger-content"
        >
          <Unlink size={17} aria-hidden />
        </IconButton>
      ) : null}
    </li>
  );
}

export function ProviderSection({
  providerLinks,
  availabilityByProviderLink,
  unlinkingId,
  lastSyncedAt,
  canManageLinks,
  onLinkProvider,
  onUnlink,
}: {
  providerLinks: MediaProviderLinkSummaryDto[];
  availabilityByProviderLink: ProviderAvailabilityMap;
  unlinkingId: string | null;
  lastSyncedAt?: string;
  canManageLinks: boolean;
  onLinkProvider: () => void;
  onUnlink: (providerId: string) => void;
}) {
  return (
    <section className="py-9">
      <DetailSectionHeading
        title="Provider identities"
        detail={
          lastSyncedAt
            ? `Last synced ${new Date(lastSyncedAt).toLocaleString()}`
            : undefined
        }
        action={canManageLinks ? "Link provider" : undefined}
        onAction={canManageLinks ? onLinkProvider : undefined}
      />
      {providerLinks.length === 0 ? (
        <div className="mt-5 flex flex-wrap items-center justify-between gap-4 py-5 text-content-muted">
          <p>No provider identities are known for this title.</p>
          {canManageLinks ? (
            <ActionButton tone="secondary" onClick={onLinkProvider}>
              <Plus size={18} aria-hidden /> Link provider
            </ActionButton>
          ) : null}
        </div>
      ) : (
        <ul className="mt-4 grid gap-x-10 md:grid-cols-2">
          {providerLinks.map((link) => (
            <ProviderRow
              key={link.id}
              link={link}
              availability={
                availabilityByProviderLink[
                  providerAvailabilityKey(link.provider, link.externalId)
                ]
              }
              canManageLinks={canManageLinks}
              isUnlinking={unlinkingId === link.provider}
              onUnlink={onUnlink}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

export function StreamingDestinationsSection({
  destinations,
  onSelect,
}: {
  destinations: readonly StreamingDestination[];
  onSelect: (serviceId: StreamingServiceId) => void;
}) {
  return (
    <section
      id="providers"
      className="py-9"
      aria-labelledby="streaming-destinations-heading"
    >
      <DetailSectionHeading
        title="Where to watch"
        detail="Verified streaming destinations linked to this title."
      />
      {destinations.length === 0 ? (
        <p className="mt-5 py-4 text-content-muted">
          No verified streaming destinations are available yet.
        </p>
      ) : (
        <ul className="mt-4 grid gap-x-10 sm:grid-cols-2">
          {destinations.map((destination) => {
            const service = STREAMING_SERVICES[destination.serviceId];
            return (
              <li
                key={`${destination.serviceId}:${destination.url}`}
                className="border-t border-border-subtle first:border-t-0 sm:[&:nth-child(2)]:border-t-0"
              >
                <a
                  href={destination.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  onClick={() => onSelect(destination.serviceId)}
                  className="group flex min-h-18 items-center gap-3 py-4 focus-visible:outline-2 focus-visible:outline-focus"
                >
                  <span className="inline-flex size-11 shrink-0 items-center justify-center bg-surface-subtle text-content transition-colors group-hover:text-personal-accent-strong">
                    <StreamingServiceIcon
                      serviceId={destination.serviceId}
                      aria-hidden
                    />
                  </span>
                  <span className="min-w-0 flex-1">
                    <strong className="block truncate text-content">
                      {service.displayName}
                    </strong>
                    <span className="mt-0.5 block text-sm text-content-muted">
                      {destination.kind === "episode"
                        ? "Episode destination"
                        : "Series destination"}
                    </span>
                  </span>
                  <ExternalLink
                    size={17}
                    className="text-content-subtle transition-colors group-hover:text-personal-accent-strong"
                    aria-hidden
                  />
                </a>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

interface CharactersSectionProps {
  entry: MediaEntryDetailModel;
  availabilityByProviderLink: ProviderAvailabilityMap;
}

interface CharacterSectionData {
  characters: MediaProviderCharacterCreditDto[];
  message: string | null;
  isError: boolean;
}

function getCharacterSectionMessage(
  supportedLinkCount: number,
  states: Array<ProviderAvailabilityState | undefined>,
  characterCount: number,
): string | null {
  if (supportedLinkCount === 0)
    return "Character credits are not supported by the linked providers.";
  if (states.some((state) => !state || state.status === "loading"))
    return "Loading character credits…";
  if (states.every((state) => state?.status === "error"))
    return "Character credits could not be loaded from AniList.";
  if (characterCount === 0)
    return "AniList has no character credits for this title.";
  return null;
}

function getCharacterSectionData({
  entry,
  availabilityByProviderLink,
}: CharactersSectionProps): CharacterSectionData {
  const supportedLinks = entry.providerLinks.filter(
    (link) => link.provider === "anilist",
  );
  const states = supportedLinks.map(
    (link) =>
      availabilityByProviderLink[
        providerAvailabilityKey(link.provider, link.externalId)
      ],
  );
  const characters = states
    .filter(
      (state): state is ProviderAvailabilityState => state?.status === "loaded",
    )
    .flatMap((state) => state.characters)
    .sort((left, right) => left.order - right.order);
  return {
    characters,
    message: getCharacterSectionMessage(
      supportedLinks.length,
      states,
      characters.length,
    ),
    isError: states.some((state) => state?.status === "error"),
  };
}

function CharacterTile({
  character,
}: {
  character: MediaProviderCharacterCreditDto;
}) {
  const content = (
    <>
      <DetailArtwork
        posterUrl={character.imageUrl}
        title={character.name}
        className="object-cover object-top transition duration-200 group-hover:scale-[1.025] group-hover:saturate-125 motion-reduce:transition-none"
      />
      <span className="absolute inset-x-0 bottom-0 bg-linear-to-t from-black/90 via-black/55 to-transparent px-3 pb-3 pt-12 text-white">
        <strong className="block truncate group-hover:text-[#ffd679]">
          {character.name}
        </strong>
        <span className="block truncate text-xs text-white/70">
          {character.role === "main" ? "Main" : "Supporting"}
        </span>
      </span>
    </>
  );
  const className =
    "group relative block aspect-[3/4] overflow-hidden bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus";
  return character.providerUrl ? (
    <a
      href={character.providerUrl}
      target="_blank"
      rel="noreferrer"
      className={className}
    >
      {content}
    </a>
  ) : (
    <div className={className}>{content}</div>
  );
}

export function CharactersSection(props: CharactersSectionProps) {
  const [showAll, setShowAll] = useState(false);
  const data = getCharacterSectionData(props);
  const visibleCharacters = showAll
    ? data.characters
    : data.characters.slice(0, 6);
  const canToggle = data.characters.length > 6;

  return (
    <section id="characters" className="py-9">
      <DetailSectionHeading
        title="Main characters"
        action={canToggle ? (showAll ? "Show primary" : "See all") : undefined}
        onAction={
          canToggle ? () => setShowAll((current) => !current) : undefined
        }
      />
      <CharacterSectionMessage message={data.message} isError={data.isError} />
      <CharacterGrid characters={visibleCharacters} />
    </section>
  );
}

function CharacterSectionMessage({
  message,
  isError,
}: {
  message: string | null;
  isError: boolean;
}) {
  if (!message) return null;
  return (
    <p className="mt-5 text-content-muted" role={isError ? "alert" : undefined}>
      {message}
    </p>
  );
}

function CharacterGrid({
  characters,
}: {
  characters: MediaProviderCharacterCreditDto[];
}) {
  if (characters.length === 0) return null;
  return (
    <ul className="mt-5 grid grid-cols-2 gap-4 sm:grid-cols-3 xl:grid-cols-6">
      {characters.map((character) => (
        <li key={character.characterId} className="min-w-0">
          <CharacterTile character={character} />
        </li>
      ))}
    </ul>
  );
}

function SummaryMetric({
  label,
  value,
  detail,
  icon,
}: {
  label: string;
  value: string;
  detail: string;
  icon?: ReactNode;
}) {
  return (
    <div className="py-4 sm:px-5 sm:first:pl-0">
      <p className="text-sm text-content-muted">{label}</p>
      <strong className="mt-2 flex items-center gap-2 text-lg text-content">
        {icon} {value}
      </strong>
      <span className="mt-1 block text-sm text-content-subtle">{detail}</span>
    </div>
  );
}

export function CommunitySection({
  entry,
  progressSummary,
}: {
  entry: MediaEntryDetailModel;
  progressSummary: ProgressSummary;
}) {
  return (
    <section className="py-9">
      <DetailSectionHeading title="Library details" />
      <div className="mt-3 grid divide-y divide-border-subtle sm:grid-cols-3 sm:divide-x sm:divide-y-0">
        <SummaryMetric
          label="Progress"
          value={`${progressSummary.value ?? 0}`}
          detail={getRemainingLabel(progressSummary)}
        />
        <SummaryMetric
          label="Library"
          value={entry.isConnected ? "Connected" : "Cantaro"}
          detail={providerLabel(entry.provider)}
          icon={<Link2 size={17} aria-hidden />}
        />
        <SummaryMetric
          label="Tracking"
          value={progressKindLabel(entry.title)}
          detail={mediaKindLabel(entry.title.mediaKind)}
        />
      </div>
    </section>
  );
}

export function InformationSection({
  entry,
}: {
  entry: MediaEntryDetailModel;
}) {
  const { title } = entry;
  const progress = getPrimaryProgressSummary(
    title,
    entry.progressEpisodes,
    entry.progressChapters,
    entry.progressVolumes,
  );
  const rows = [
    ["Format", title.format ?? progressKindLabel(title)],
    ["Status", releaseStatusLabel(title.releaseStatusDimension)],
    ["Started", title.startYear ? String(title.startYear) : "Unknown"],
    ["Provider", providerLabel(entry.provider)],
    [
      "Total",
      progress.total ? `${progress.total} ${progress.noun}` : "Unknown",
    ],
    ["Original title", title.originalTitle ?? "Not provided"],
  ];

  return (
    <section className="py-9" aria-labelledby="media-information-heading">
      <h2
        id="media-information-heading"
        className="text-xl font-bold text-content"
      >
        Information
      </h2>
      <dl className="mt-5 grid gap-x-8 gap-y-4 sm:grid-cols-2">
        {rows.map(([label, value]) => (
          <div key={label} className="grid grid-cols-[6rem_1fr] gap-3 text-sm">
            <dt className="text-content-subtle">{label}</dt>
            <dd className="min-w-0 break-words font-semibold text-content">
              {value}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
