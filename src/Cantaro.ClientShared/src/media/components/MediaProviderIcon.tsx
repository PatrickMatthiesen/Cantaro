import type { SVGProps } from 'react';
import Anilist from '@thesvg/react/anilist';
import type { MediaProviderId } from '../services/mediaProviders';

export function MediaProviderIcon({
    providerId,
    className = 'h-5 w-5',
    ...props
}: SVGProps<SVGSVGElement> & { providerId: MediaProviderId }) {
    switch (providerId) {
        case 'anilist':
            return <Anilist className={className} {...props} />;
    }
}
