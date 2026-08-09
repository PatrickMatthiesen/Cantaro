export type ExtensionMessageErrorCode =
  | 'unsupported_tab'
  | 'invalid_request'
  | 'not_authenticated'
  | 'delivery_failed'
  | 'api_rejected'
  | 'not_found'
  | 'unexpected';

export interface ExtensionMessageError {
  code: ExtensionMessageErrorCode;
  message: string;
  retryable?: boolean;
}

export type MessageResult<T> =
  | { ok: true; value: T; correlationId: string }
  | { ok: false; error: ExtensionMessageError; correlationId: string };

export function messageSuccess<T>(value: T, correlationId: string): MessageResult<T> {
  return { ok: true, value, correlationId };
}

export function messageFailure(
  correlationId: string,
  code: ExtensionMessageErrorCode,
  message: string,
  retryable = false,
): MessageResult<never> {
  return { ok: false, error: { code, message, retryable }, correlationId };
}

export function createCorrelationId(): string {
  return crypto.randomUUID();
}
