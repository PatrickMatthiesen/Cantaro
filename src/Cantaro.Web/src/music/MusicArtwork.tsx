import { useState } from 'react';
import { MusicUiIcon } from '@cantaro/client-shared/music';
import { isYouTubeThumbnailPlaceholder } from './musicPresentation';

export function MusicArtwork({ src, fallbacks = [], className, contain, backdrop }: {
  src?: string;
  fallbacks?: string[];
  className: string;
  contain?: boolean;
  backdrop?: boolean;
}) {
  const [failedSources, setFailedSources] = useState<string[]>([]);
  const currentSrc = [src, ...fallbacks].find((candidate) => candidate && !failedSources.includes(candidate));
  const rejectImage = () => {
    if (currentSrc) setFailedSources((previous) => [...previous, currentSrc]);
  };

  return (
    <span className={`relative block shrink-0 overflow-hidden ${backdrop ? '' : 'bg-surface-subtle text-content-subtle'} ${className}`} aria-hidden>
      {backdrop ? null : <MusicUiIcon name="music" className="absolute inset-0 m-auto size-1/2 max-h-12 max-w-12" />}
      {currentSrc ? (
        <img
          key={currentSrc}
          src={currentSrc}
          alt=""
          loading="lazy"
          onError={rejectImage}
          onLoad={({ currentTarget }) => {
            if (isYouTubeThumbnailPlaceholder(currentSrc, currentTarget.naturalWidth, currentTarget.naturalHeight)) rejectImage();
          }}
          className={`absolute inset-0 size-full ${backdrop ? '' : 'bg-surface-subtle'} ${contain ? 'object-contain' : 'object-cover'}`}
        />
      ) : null}
    </span>
  );
}
