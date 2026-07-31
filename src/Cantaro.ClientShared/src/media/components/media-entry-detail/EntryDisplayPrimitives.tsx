import { useState } from 'react';
import DOMPurify from 'dompurify';
import { MediaProviderIcon } from '../MediaProviderIcon';

export function SanitizedSynopsis({ html, className }: { html: string; className?: string }) {
    const sanitizedHtml = DOMPurify.sanitize(html, {
        ALLOWED_TAGS: ['br', 'i', 'em', 'b', 'strong'],
        ALLOWED_ATTR: [],
    });

    return <div className={className} dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />;
}

export function DetailArtwork({ posterUrl, title }: { posterUrl?: string; title: string }) {
    const [failed, setFailed] = useState(false);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center rounded-2xl bg-surface-subtle">
                <MediaProviderIcon providerId="anilist" className="h-12 w-12" aria-hidden />
                <span className="sr-only">{title} — no artwork available</span>
            </div>
        );
    }

    return (
        <img
            src={posterUrl}
            alt={`${title} cover art`}
            className="h-full w-full rounded-2xl object-cover"
            onError={() => setFailed(true)}
        />
    );
}
