import { useState } from "react";
import DOMPurify from "dompurify";
import { ImageIcon } from "lucide-react";
import { MediaProviderIcon } from "../MediaProviderIcon";
import { mediaProviderIconId } from "../../services/mediaProviders";

export function SanitizedSynopsis({
  html,
  className,
}: {
  html: string;
  className?: string;
}) {
  const sanitizedHtml = DOMPurify.sanitize(html, {
    ALLOWED_TAGS: ["br", "i", "em", "b", "strong"],
    ALLOWED_ATTR: [],
  });

  return (
    <div
      className={className}
      dangerouslySetInnerHTML={{ __html: sanitizedHtml }}
    />
  );
}

export function DetailArtwork({
  posterUrl,
  title,
  providerId,
  className = "",
}: {
  posterUrl?: string;
  title: string;
  providerId?: string;
  className?: string;
}) {
  const [failed, setFailed] = useState(false);
  const iconId = mediaProviderIconId(providerId);

  if (!posterUrl || failed) {
    return (
      <div
        className={`flex h-full w-full items-center justify-center bg-surface-subtle ${className}`}
      >
        {iconId ? (
          <MediaProviderIcon providerId={iconId} className="h-12 w-12" aria-hidden />
        ) : (
          <ImageIcon className="h-12 w-12 text-content-muted" aria-hidden />
        )}
        <span className="sr-only">{title} — no artwork available</span>
      </div>
    );
  }

  return (
    <img
      src={posterUrl}
      alt={`${title} cover art`}
      className={`h-full w-full object-cover ${className}`}
      onError={() => setFailed(true)}
    />
  );
}
