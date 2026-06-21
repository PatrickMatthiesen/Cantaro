import type { SVGProps } from 'react';
import Anilist from '@thesvg/react/anilist';
import type { MediaProviderId } from '../services/mediaProviders';

export function MediaProviderIcon({
    providerId,
    className = 'h-5 w-5',
    variant = 'brand',
    style,
    ...props
}: SVGProps<SVGSVGElement> & { providerId: MediaProviderId; variant?: 'brand' | 'mono' }) {
    switch (providerId) {
        case 'anilist':
            return (
                <Anilist
                    className={className}
                    style={{ ...style, fill: variant === 'mono' ? 'currentColor' : style?.fill }}
                    {...props}
                />
            );
    }
}
