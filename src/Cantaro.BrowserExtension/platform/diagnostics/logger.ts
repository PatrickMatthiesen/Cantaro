import { extensionLogMessage } from './extensionIdentity';
import { stripUrlQueryAndFragment } from '../../features/media/contracts/observationUrl';

export interface DiagnosticContext {
  scope: 'background' | 'popup' | 'content';
  feature?: 'media' | 'music' | 'settings' | 'auth';
  provider?: string;
  tabId?: number;
  documentId?: string;
  correlationId?: string;
}

export interface ExtensionLogger {
  debug(message: string, details?: unknown): void;
  info(message: string, details?: unknown): void;
  warn(message: string, details?: unknown): void;
  error(message: string, error?: unknown): void;
  child(context: Partial<DiagnosticContext>): ExtensionLogger;
}

/** Keeps exception objects and URL credentials/query strings out of diagnostics. */
export function sanitizeDiagnosticDetails(value: unknown): unknown {
  return sanitizeDiagnosticValue(value, new WeakSet<object>(), 0);
}

function sanitizeDiagnosticValue(
  value: unknown,
  seen: WeakSet<object>,
  depth: number,
): unknown {
  if (depth > 6) return '[truncated]';
  if (value instanceof Error) {
    return { name: value.name || 'Error' };
  }
  if (typeof value === 'string') {
    return sanitizeDiagnosticText(value);
  }
  if (Array.isArray(value)) {
    if (seen.has(value)) return '[circular]';
    seen.add(value);
    return value.map(entry => sanitizeDiagnosticValue(entry, seen, depth + 1));
  }
  if (value && typeof value === 'object') {
    if (seen.has(value)) return '[circular]';
    seen.add(value);
    return Object.fromEntries(
      Object.entries(value).map(([key, entry]) => [
        key,
        sanitizeDiagnosticValue(entry, seen, depth + 1),
      ]),
    );
  }
  return value;
}

function sanitizeDiagnosticText(value: string): string {
  try {
    const url = new URL(value);
    if (url.protocol === 'http:' || url.protocol === 'https:') {
      return stripUrlQueryAndFragment(value);
    }
  } catch {
    // The value is ordinary diagnostic text rather than a URL.
  }
  return value.replace(/https?:\/\/[^\s"'<>]+/gi, (candidate) => {
    try {
      return stripUrlQueryAndFragment(candidate);
    } catch {
      return '[url]';
    }
  });
}

function write(
  level: 'debug' | 'info' | 'warn' | 'error',
  context: DiagnosticContext,
  verbose: () => boolean,
  message: string,
  details?: unknown,
): void {
  if (level === 'debug' && !verbose()) return;
  const payload = details === undefined
    ? context
    : { ...context, details: sanitizeDiagnosticDetails(details) };
  console[level](extensionLogMessage(sanitizeDiagnosticText(message)), payload);
}

export function createExtensionLogger(
  context: DiagnosticContext,
  verbose: () => boolean = () => false,
): ExtensionLogger {
  return {
    debug: (message, details) => write('debug', context, verbose, message, details),
    info: (message, details) => write('info', context, verbose, message, details),
    warn: (message, details) => write('warn', context, verbose, message, details),
    error: (message, error) => write('error', context, verbose, message, error),
    child: (childContext) => createExtensionLogger({ ...context, ...childContext }, verbose),
  };
}
