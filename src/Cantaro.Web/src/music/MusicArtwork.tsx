import { useState } from 'react';
import { MusicUiIcon } from '@cantaro/client-shared/music';

export function MusicArtwork({ src, className, contain }: {
  src?: string;
  className: string;
  contain?: boolean;
}) {
  const [failedSrc, setFailedSrc] = useState<string>();
  return (
    <span className={`relative block shrink-0 overflow-hidden bg-surface-subtle text-content-subtle ${className}`} aria-hidden>
      <MusicUiIcon name="music" className="absolute inset-0 m-auto size-1/2 max-h-12 max-w-12" />
      {src && src !== failedSrc ? (
        <img
          src={src}
          alt=""
          loading="lazy"
          onError={() => setFailedSrc(src)}
          className={`absolute inset-0 size-full bg-surface-subtle ${contain ? 'object-contain' : 'object-cover'}`}
        />
      ) : null}
    </span>
  );
}
