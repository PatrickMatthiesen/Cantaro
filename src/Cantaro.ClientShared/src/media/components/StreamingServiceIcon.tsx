import type { CSSProperties, SVGProps } from 'react';
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from '../services/streamingServices';

function getOpticalClassName(serviceId: StreamingServiceId): string {
  return serviceId === 'apple-tv' ? ' dark:invert' : '';
}

function getDefaultFill(serviceId: StreamingServiceId, iconColor: string): string {
  return serviceId === 'stremio' ? 'none' : iconColor;
}

function getIconStyle(serviceId: StreamingServiceId, style?: CSSProperties): CSSProperties {
  const iconColor = style?.color ?? STREAMING_SERVICES[serviceId].brandColor;
  return { color: iconColor, fill: style?.fill ?? getDefaultFill(serviceId, iconColor), ...style };
}

export function StreamingServiceIcon({
  serviceId,
  className = 'h-5 w-5',
  style,
  ...props
}: SVGProps<SVGSVGElement> & { serviceId: StreamingServiceId }) {
  const service = STREAMING_SERVICES[serviceId];
  if (serviceId === 'aiostreams') {
    return <img src="https://aiostreams.org/logo.png" alt="" className={className} style={style} aria-hidden />;
  }
  const Icon = service.icon;
  if (!Icon) {
    return (
      <span
        className={className}
        style={{ color: service.brandColor, ...style } as CSSProperties}
        aria-hidden
      >
        {service.displayName.slice(0, 1)}
      </span>
    );
  }
  return (
    <Icon
      className={`${className}${getOpticalClassName(serviceId)}`}
      style={getIconStyle(serviceId, style)}
      {...props}
    />
  );
}
