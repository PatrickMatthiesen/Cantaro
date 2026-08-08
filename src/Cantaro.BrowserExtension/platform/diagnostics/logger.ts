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

function write(
  level: 'debug' | 'info' | 'warn' | 'error',
  context: DiagnosticContext,
  verbose: () => boolean,
  message: string,
  details?: unknown,
): void {
  if (level === 'debug' && !verbose()) return;
  const payload = details === undefined ? context : { ...context, details };
  console[level](`Cantaro: ${message}`, payload);
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
