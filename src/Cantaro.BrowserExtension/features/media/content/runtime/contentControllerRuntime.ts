import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import { extensionLogMessage } from '../../../../platform/diagnostics/extensionIdentity';
import { sanitizeDiagnosticDetails } from '../../../../platform/diagnostics/logger';
import { registerTabContext } from '../../../../platform/messaging/tabContext';
import type { MediaTabContext } from '../../contracts/mediaTabContext';

export interface VerboseLoggingDependencies {
  readVerboseLogging(): Promise<boolean>;
  watchVerboseLogging(listener: (enabled: boolean) => void): () => void;
}

export function logContentVerbose(enabled: boolean, message: string, details?: unknown): void {
  if (!enabled) return;
  if (details === undefined) console.debug(extensionLogMessage(message));
  else console.debug(extensionLogMessage(message), details);
}

function startVerboseLogging(
  ctx: ContentScriptContext,
  dependencies: VerboseLoggingDependencies,
  setEnabled: (enabled: boolean) => void,
  onReady: () => void,
  canLog: () => boolean,
): void {
  const stopWatching = dependencies.watchVerboseLogging((enabled) => {
    setEnabled(enabled);
    if (canLog()) {
      console.info(extensionLogMessage(`verbose logging ${enabled ? 'enabled' : 'disabled'}`));
    }
  });
  ctx.onInvalidated(stopWatching);

  void dependencies.readVerboseLogging()
    .then(setEnabled)
    .catch((error: unknown) => {
      console.warn(extensionLogMessage('could not read verbose logging setting'), sanitizeDiagnosticDetails(error));
    })
    .finally(onReady);
}

export function startMediaControllerRuntime(
  ctx: ContentScriptContext,
  dependencies: VerboseLoggingDependencies,
  getSnapshot: () => MediaTabContext,
  setVerboseLogging: (enabled: boolean) => void,
  onReady: () => void,
  isActive: () => boolean = () => true,
  canLog: () => boolean = () => true,
): () => void {
  const notifyChanged = registerTabContext(ctx, getSnapshot, isActive);
  startVerboseLogging(ctx, dependencies, setVerboseLogging, onReady, canLog);
  return () => {
    if (canLog()) notifyChanged();
  };
}
