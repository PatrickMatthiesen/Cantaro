import type { CSSProperties, SVGProps } from 'react';
import {
  STREAMING_SERVICES,
  type StreamingServiceId,
} from '../services/streamingServices';

export function StreamingServiceIcon({
  serviceId,
  className = 'h-5 w-5',
  style,
  ...props
}: SVGProps<SVGSVGElement> & { serviceId: StreamingServiceId }) {
  const service = STREAMING_SERVICES[serviceId];
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
  const iconColor = style?.color ?? service.brandColor;
  return (
    <Icon
      className={className}
      style={{ color: iconColor, fill: style?.fill ?? iconColor, ...style }}
      {...props}
    />
  );
}
