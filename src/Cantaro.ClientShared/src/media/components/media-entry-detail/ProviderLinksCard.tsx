import { GradientButton, GlassCard } from '../../../ui';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import { MediaProviderIcon } from '../MediaProviderIcon';
import type { MediaProviderLinkSummaryDto } from '../../services/mediaApi';
import type { ProviderAvailabilityMap, ProviderAvailabilityState } from './providerAvailability';
import { providerAvailabilityKey } from './providerAvailability';

function ProviderAvailabilityLinkChip({
    displayName,
    url,
    title,
}: {
    linkId: string;
    serviceId: string;
    displayName: string;
    url?: string | null;
    title?: string;
}){
    const chipClasses = 'inline-flex items-center rounded-full bg-sky-50 px-2.5 py-1 text-xs font-medium text-sky-700 hover:bg-sky-100';

    if (url) {
        return (
            <a
                href={url}
                target="_blank"
                rel="noopener noreferrer"
                className={chipClasses}
                title={title}
            >
                {displayName} ↗
            </a>
        );
    }

    return <span className={chipClasses} title={title}>{displayName}</span>;
}

function ProviderAvailabilityMessage({ availability }: { availability?: ProviderAvailabilityState }) {
    if (availability?.status === 'loading') {
        return <p className="mt-1 text-xs text-gray-400">Loading availability…</p>;
    }

    if (availability?.status === 'error') {
        return (
            <p className="mt-1 text-xs text-gray-400" title={availability.error}>
                Availability unavailable
            </p>
        );
    }

    return null;
}

function ProviderAvailabilityLinks({
    linkId,
    availability,
}: {
    linkId: string;
    availability?: ProviderAvailabilityState;
}) {
    const message = <ProviderAvailabilityMessage availability={availability} />;
    if (message) {
        return message;
    }

    if (!availability || availability.status !== 'loaded' || availability.links.length === 0) {
        return null;
    }

    return (
        <div className="mt-2 flex flex-wrap gap-2">
            {availability.links.map((availabilityLink) => {
                return (
                    <ProviderAvailabilityLinkChip
                        key={`${linkId}:${availabilityLink.serviceId}`}
                        linkId={linkId}
                        serviceId={availabilityLink.serviceId}
                        displayName={availabilityLink.displayName}
                        url={availabilityLink.url}
                        title={availabilityLink.notes ?? availabilityLink.availabilityKind}
                    />
                );
            })}
        </div>
    );
}

function ProviderLinkIdentity({ link }: { link: MediaProviderLinkSummaryDto }) {
    const catalog = mediaProviderCatalog.find((provider) => provider.id === link.provider);

    return (
        <>
            {catalog ? <MediaProviderIcon providerId={catalog.iconId} className="h-5 w-5 shrink-0" aria-hidden /> : null}
            <div className="min-w-0">
                <p className="text-sm font-medium text-gray-800">{catalog?.name ?? link.provider}</p>
                <p className="text-xs text-gray-500">
                    ID: {link.externalId}
                    {link.linkSource !== 'manual' ? null : ' · manual'}
                </p>
            </div>
        </>
    );
}

function ProviderLinkActions({
    externalUrl,
    isUnlinking,
    onUnlink,
}: {
    externalUrl?: string | null;
    isUnlinking: boolean;
    onUnlink: () => void;
}) {
    return (
        <div className="flex shrink-0 items-center gap-2">
            {externalUrl ? (
                <a
                    href={externalUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-xs text-indigo-500 hover:underline"
                >
                    Open ↗
                </a>
            ) : null}
            <button
                type="button"
                className="rounded-lg px-3 py-1.5 text-xs text-rose-600 transition hover:bg-rose-50 disabled:opacity-50"
                disabled={isUnlinking}
                onClick={onUnlink}
            >
                {isUnlinking ? '…' : 'Unlink'}
            </button>
        </div>
    );
}

function ProviderLinkRow({
    link,
    availability,
    isUnlinking,
    onUnlink,
}: {
    link: MediaProviderLinkSummaryDto;
    availability?: ProviderAvailabilityState;
    isUnlinking: boolean;
    onUnlink: (providerId: string) => void;
}) {
    return (
        <li className="flex items-center justify-between gap-3 rounded-xl bg-white/70 px-4 py-3">
            <div className="flex min-w-0 items-center gap-3">
                <div className="flex min-w-0 items-center gap-3">
                    <ProviderLinkIdentity link={link} />
                    <ProviderAvailabilityLinks linkId={link.id} availability={availability} />
                </div>
            </div>
            <ProviderLinkActions externalUrl={link.externalUrl} isUnlinking={isUnlinking} onUnlink={() => onUnlink(link.provider)} />
        </li>
    );
}

export interface ProviderLinksCardProps {
    providerLinks: MediaProviderLinkSummaryDto[];
    availabilityByProviderLink: ProviderAvailabilityMap;
    unlinkingId: string | null;
    lastSyncedAt?: string;
    onLinkProvider: () => void;
    onUnlink: (providerId: string) => void;
}

export function ProviderLinksCard({
    providerLinks,
    availabilityByProviderLink,
    unlinkingId,
    lastSyncedAt,
    onLinkProvider,
    onUnlink,
}: ProviderLinksCardProps) {
    return (
        <GlassCard className="p-6">
            <div className="flex items-center justify-between gap-3">
                <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Provider links</h2>
                <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onLinkProvider}>
                    + Link provider
                </GradientButton>
            </div>

            {providerLinks.length === 0 ? (
                <p className="mt-4 text-sm text-gray-400 italic">
                    No provider links. Use "Link provider" to connect this entry to an external service.
                </p>
            ) : (
                <ul className="mt-4 space-y-2">
                    {providerLinks.map((link) => (
                        <ProviderLinkRow
                            key={link.id}
                            link={link}
                            availability={availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)]}
                            isUnlinking={unlinkingId === link.provider}
                            onUnlink={onUnlink}
                        />
                    ))}
                </ul>
            )}

            {lastSyncedAt ? <p className="mt-3 text-xs text-gray-400">Last synced {new Date(lastSyncedAt).toLocaleString()}</p> : null}
        </GlassCard>
    );
}
