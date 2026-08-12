import { useState, type ReactNode } from 'react';
import { CircleCheck, Flame, Heart, MoreVertical, Plus, Star } from 'lucide-react';
import { DetailArtwork } from '../../components/media-entry-detail/EntryDisplayPrimitives';
import {
  providerAvailabilityKey,
  type ProviderAvailabilityMap,
  type ProviderAvailabilityState,
} from '../../components/media-entry-detail/providerAvailability';
import { MediaProviderIcon } from '../../components/MediaProviderIcon';
import { mediaKindLabel } from '../../services/mediaFormatting';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type {
  MediaEntryDetailModel,
  MediaProviderCharacterCreditDto,
  MediaProviderLinkSummaryDto,
} from '../../services/mediaApi';
import {
  getProgressPercent,
  getPrimaryProgressSummary,
  getRemainingLabel,
  progressKindLabel,
  releaseStatusLabel,
} from './mediaEntryDetailModel';
import type { ProgressSummary } from './mediaEntryDetailTypes';

function providerLabel(providerId: string) {
  return mediaProviderCatalog.find((provider) => provider.id === providerId)?.name ?? providerId;
}
function availabilityText(availability?: ProviderAvailabilityState) {
  if (!availability || availability.status === 'loading') {
    return 'Checking';
  }

  if (availability.status === 'error') {
    return 'Unavailable';
  }

  return availability.links.length > 0 ? `${availability.links.length} options` : 'Linked';
}

function ProviderCard({
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
  const catalog = mediaProviderCatalog.find((provider) => provider.id === link.provider);
  return (
    <article className="media-detail-provider-card">
      {catalog
        ? <MediaProviderIcon providerId={catalog.iconId} aria-hidden />
        : <span className="media-detail-provider-letter">{link.provider.slice(0, 1).toUpperCase()}</span>}
      <div>
        <h4>{providerLabel(link.provider)}</h4>
        <p>{availabilityText(availability)}</p>
        {link.externalUrl ? <a href={link.externalUrl} target="_blank" rel="noopener noreferrer">Open</a> : null}
      </div>
      {canManageLinks ? (
        <button type="button" onClick={() => onUnlink(link.provider)} disabled={isUnlinking}>
          {isUnlinking ? '...' : 'Unlink'}
        </button>
      ) : null}
      <CircleCheck aria-hidden className="media-detail-provider-check" />
    </article>
  );
}

function EmptyProviderState({ canManageLinks, onLinkProvider }: {
  canManageLinks: boolean;
  onLinkProvider: () => void;
}) {
  if (!canManageLinks) return <p>No provider identities are known for this title.</p>;
  return (
    <button type="button" className="media-detail-empty-provider" onClick={onLinkProvider}>
      <Plus aria-hidden />
      Link a provider to show availability.
    </button>
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
  const visibleLinks = providerLinks.slice(0, 3);
  const hiddenCount = Math.max(providerLinks.length - visibleLinks.length, 0);

  return (
    <section className="media-detail-section">
      <SectionHeading title="Provider identities" action={canManageLinks ? 'Manage links' : undefined} onAction={canManageLinks ? onLinkProvider : undefined} />
      {providerLinks.length === 0 ? (
        <EmptyProviderState canManageLinks={canManageLinks} onLinkProvider={onLinkProvider} />
      ) : (
        <div className="media-detail-provider-grid">
          {visibleLinks.map((link) => {
            const availability = availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)];
            return (
              <ProviderCard
                key={link.id}
                link={link}
                availability={availability}
                canManageLinks={canManageLinks}
                isUnlinking={unlinkingId === link.provider}
                onUnlink={onUnlink}
              />
            );
          })}
          {hiddenCount > 0 ? (
            <button type="button" className="media-detail-provider-more" onClick={onLinkProvider} disabled={!canManageLinks}>
              <MoreVertical aria-hidden />
              More
              <span>{hiddenCount}+</span>
            </button>
          ) : null}
        </div>
      )}
      {lastSyncedAt ? <p className="media-detail-section-note">Last synced {new Date(lastSyncedAt).toLocaleString()}</p> : null}
    </section>
  );
}

function SectionHeading({ title, action, onAction }: { title: string; action?: string; onAction?: () => void }) {
  return (
    <div className="media-detail-section-heading">
      <h3>{title}</h3>
      {action ? <button type="button" onClick={onAction}>{action}</button> : null}
    </div>
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
  if (supportedLinkCount === 0) return 'Character credits are not supported by the linked providers.';
  if (states.some((state) => !state || state.status === 'loading')) return 'Loading character credits…';
  if (states.every((state) => state?.status === 'error')) return 'Character credits could not be loaded from AniList.';
  if (characterCount === 0) return 'AniList has no character credits for this title.';
  return null;
}

function getCharacterSectionData({ entry, availabilityByProviderLink }: CharactersSectionProps): CharacterSectionData {
  const supportedLinks = entry.providerLinks.filter((link) => link.provider === 'anilist');
  const states = supportedLinks.map((link) => availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)]);
  const characters = states
    .filter((state): state is ProviderAvailabilityState => state?.status === 'loaded')
    .flatMap((state) => state.characters)
    .sort((left, right) => left.order - right.order);

  return {
    characters,
    message: getCharacterSectionMessage(supportedLinks.length, states, characters.length),
    isError: states.some((state) => state?.status === 'error'),
  };
}

function CharacterCard({ character }: { character: MediaProviderCharacterCreditDto }) {
  const name = character.providerUrl
    ? <a href={character.providerUrl} target="_blank" rel="noreferrer">{character.name}</a>
    : character.name;

  return (
    <article className="media-detail-character-card">
      <div><DetailArtwork posterUrl={character.imageUrl} title={character.name} /></div>
      <h4>{name}</h4>
      <p>{character.role === 'main' ? 'Main' : 'Supporting'}</p>
    </article>
  );
}

function CharacterList({ characters }: { characters: MediaProviderCharacterCreditDto[] }) {
  if (characters.length === 0) return null;

  return (
    <div className="media-detail-character-row">
      {characters.map((character) => <CharacterCard key={character.characterId} character={character} />)}
    </div>
  );
}

export function CharactersSection(props: CharactersSectionProps) {
  const [showAll, setShowAll] = useState(false);
  const data = getCharacterSectionData(props);
  const visibleCharacters = showAll ? data.characters : data.characters.slice(0, 8);

  return (
    <section className="media-detail-section">
      <SectionHeading title="Characters" />
      {data.message ? <p className="media-detail-character-state" role={data.isError ? 'alert' : undefined}>{data.message}</p> : null}
      <CharacterList characters={visibleCharacters} />
      {data.characters.length > 8 ? (
        <button type="button" className="media-detail-character-more" onClick={() => setShowAll((current) => !current)}>
          {showAll ? 'Show primary characters' : `View all ${data.characters.length} characters`}
        </button>
      ) : null}
    </section>
  );
}

export function CommunitySection({ entry, progressSummary }: { entry: MediaEntryDetailModel; progressSummary: ProgressSummary }) {
  return (
    <section className="media-detail-community">
      <SectionHeading title="Community" action="See all" />
      <div className="media-detail-community-grid">
        <MetricCard icon={<Star aria-hidden />} label="Progress" value={`${getProgressPercent(progressSummary)}%`} detail={getRemainingLabel(progressSummary)} />
        <MetricCard icon={<Heart aria-hidden />} label="Library" value={entry.isConnected ? 'Synced' : 'Local'} detail={providerLabel(entry.provider)} />
        <MetricCard icon={<Flame aria-hidden />} label="Status" value={progressKindLabel(entry.title)} detail={mediaKindLabel(entry.title.mediaKind)} />
      </div>
    </section>
  );
}

export function InformationSection({ entry }: { entry: MediaEntryDetailModel }) {
  const { title } = entry;
  const rows = [
    ['Format', progressKindLabel(title)],
    ['Status', releaseStatusLabel(title.releaseStatusDimension)],
    ['Aired', title.startYear ? String(title.startYear) : 'Unknown'],
    ['Provider', providerLabel(entry.provider)],
    ['Progress', getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).total ? `${getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).total} ${getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).noun}` : 'Unknown'],
    ['Rating', mediaKindLabel(title.mediaKind)],
  ];

  return (
    <section className="media-detail-info-card">
      <h3>Information</h3>
      <dl>
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function MetricCard({ icon, label, value, detail }: { icon: ReactNode; label: string; value: string; detail: string }) {
  return (
    <article className="media-detail-metric-card">
      <p>{label}</p>
      <strong>{icon}{value}</strong>
      <span>{detail}</span>
    </article>
  );
}
