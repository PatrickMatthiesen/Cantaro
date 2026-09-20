import type { SVGProps } from 'react';
import Anilist from '@thesvg/react/anilist';
import Myanimelist from '@thesvg/react/myanimelist';
import Simkl from '@thesvg/react/simkl';
import type { MediaProviderId } from '../services/mediaProviders';

const providerIcons = {
    anilist: Anilist,
    myanimelist: Myanimelist,
    simkl: Simkl,
} satisfies Record<MediaProviderId, typeof Anilist>;

export function MediaProviderIcon({
    providerId,
    className = 'h-5 w-5',
    variant = 'brand',
    style,
    ...props
}: SVGProps<SVGSVGElement> & { providerId: MediaProviderId; variant?: 'brand' | 'mono' }) {
    const Icon = providerIcons[providerId];
    return (
        <Icon
            className={className}
            style={{ ...style, fill: variant === 'mono' ? 'currentColor' : style?.fill }}
            {...props}
        />
    );
}
