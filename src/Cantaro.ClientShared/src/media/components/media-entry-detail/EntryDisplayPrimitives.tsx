import { useState } from 'react';
import DOMPurify from 'dompurify';

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
            <div className="flex h-full w-full items-center justify-center rounded-2xl bg-linear-to-br from-indigo-100 to-purple-100">
                <span className="text-4xl" aria-hidden>🎌</span>
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
