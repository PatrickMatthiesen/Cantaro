export interface DrainDeliveryQueueRequest {
  type: 'delivery.queue.drain';
  correlationId: string;
}
export interface DrainDeliveryQueueResult {
  delivered: number;
  remaining: number;
}

export function isDrainDeliveryQueueRequest(value: unknown): value is DrainDeliveryQueueRequest {
  return Boolean(value && typeof value === 'object'
    && (value as { type?: unknown }).type === 'delivery.queue.drain'
    && typeof (value as { correlationId?: unknown }).correlationId === 'string');
}
