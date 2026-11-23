import { useEffect, useState } from 'react';
import { Check } from 'lucide-react';
import { ConnectionCard, type ConnectionState } from './ConnectionCard';
import { aioStreamsPairingEndpoint, getAioStreamsPairingStatus } from '../media/aioStreamsPairing';

const buttonClass = 'inline-flex min-h-10 items-center justify-center px-4 text-xs font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:opacity-60';
const primaryButton = `${buttonClass} bg-personal-accent text-personal-accent-content enabled:cursor-pointer enabled:hover:bg-personal-accent-hover`;
const disconnectButton = `${buttonClass} border border-danger-border text-danger-content enabled:cursor-pointer enabled:hover:bg-danger-surface`;

function getPairingAction(paired: boolean | null, error: string): ConnectionState {
  if (paired) return 'manage';
  return paired === null && error ? 'retry' : 'connect';
}

function isPairingActionBusy(busy: boolean, paired: boolean | null, error: string): boolean {
  return busy || (paired === null && !error);
}

function getPairingDetail(paired: boolean | null, savedInstanceUrl: string): string {
  if (savedInstanceUrl) return 'Playback and watch history';
  return paired ? 'Pairing link active' : 'Watch progress and history';
}

export function AioStreamsPairingPanel() {
  const [paired, setPaired] = useState<boolean | null>(null);
  const [manifestUrl, setManifestUrl] = useState('');
  const [instanceUrl, setInstanceUrl] = useState('');
  const [savedInstanceUrl, setSavedInstanceUrl] = useState('');
  const [showManage, setShowManage] = useState(false);
  const [busy, setBusy] = useState(false);
  const [copiedAt, setCopiedAt] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [statusAttempt, setStatusAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    void getAioStreamsPairingStatus(controller.signal)
      .then(status => {
        if (controller.signal.aborted) return;
        setPaired(status.paired);
        setInstanceUrl(status.playbackInstanceUrl ?? '');
        setSavedInstanceUrl(status.playbackInstanceUrl ?? '');
      })
      .catch(() => {
        if (!controller.signal.aborted) setError('Could not load the AIOStreams connection.');
      });
    return () => controller.abort();
  }, [statusAttempt]);

  useEffect(() => {
    if (copiedAt === null) return;
    const timeout = window.setTimeout(() => setCopiedAt(null), 3000);
    return () => window.clearTimeout(timeout);
  }, [copiedAt]);

  async function update(method: 'PUT' | 'DELETE') {
    setBusy(true);
    setCopiedAt(null);
    setError('');
    try {
      const response = await fetch(aioStreamsPairingEndpoint, { method });
      if (!response.ok) throw new Error();
      if (method === 'PUT') {
        const pairing = await response.json() as { manifestPath: string };
        setManifestUrl(new URL(pairing.manifestPath, window.location.origin).href);
        setPaired(true);
        setShowManage(true);
        return;
      }
      setPaired(false);
      setManifestUrl('');
      setInstanceUrl('');
      setSavedInstanceUrl('');
      setShowManage(false);
    } catch {
      setError('Could not update the AIOStreams connection.');
    } finally {
      setBusy(false);
    }
  }

  async function saveInstance() {
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`${aioStreamsPairingEndpoint}/playback`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ instanceUrl }),
      });
      if (response.status === 400) {
        setError('Enter the HTTPS address of your AIOStreams instance.');
        return;
      }
      if (!response.ok) throw new Error();
      const settings = await response.json() as { playbackInstanceUrl?: string };
      setInstanceUrl(settings.playbackInstanceUrl ?? '');
      setSavedInstanceUrl(settings.playbackInstanceUrl ?? '');
    } catch {
      setError('Could not save the AIOStreams playback address.');
    } finally {
      setBusy(false);
    }
  }

  function handleConnectionAction() {
    if (paired === null) {
      setError('');
      setStatusAttempt(attempt => attempt + 1);
      return;
    }
    if (paired) {
      setShowManage(show => !show);
      return;
    }
    void update('PUT');
  }

  async function copyManifestUrl() {
    setError('');
    try {
      await navigator.clipboard.writeText(manifestUrl);
      setCopiedAt(Date.now());
    } catch {
      setError('Select and copy the URL from the field.');
    }
  }

  return (
    <div>
      <ConnectionCard
        name="AIOStreams"
        detail={getPairingDetail(paired, savedInstanceUrl)}
        icon={<img src="https://aiostreams.org/logo.png" alt="" className="size-10 shrink-0 object-contain" />}
        implemented
        state={getPairingAction(paired, error)}
        busy={isPairingActionBusy(busy, paired, error)}
        onAction={handleConnectionAction}
      />
      {paired && showManage && (
        <PairingManagement
          busy={busy}
          instanceUrl={instanceUrl}
          savedInstanceUrl={savedInstanceUrl}
          manifestUrl={manifestUrl}
          onInstanceUrlChange={setInstanceUrl}
          onSaveInstance={saveInstance}
          onUpdatePairing={update}
          onCopyManifestUrl={copyManifestUrl}
        />
      )}
      <PairingFeedback copied={copiedAt !== null} error={error} />
    </div>
  );
}

function PairingManagement({
  busy, instanceUrl, savedInstanceUrl, manifestUrl,
  onInstanceUrlChange, onSaveInstance, onUpdatePairing, onCopyManifestUrl,
}: {
  busy: boolean;
  instanceUrl: string;
  savedInstanceUrl: string;
  manifestUrl: string;
  onInstanceUrlChange: (url: string) => void;
  onSaveInstance: () => Promise<void>;
  onUpdatePairing: (method: 'PUT' | 'DELETE') => Promise<void>;
  onCopyManifestUrl: () => Promise<void>;
}) {
  return (
    <>
      <div className="flex flex-wrap gap-2 pb-3">
        <button type="button" disabled={busy} onClick={() => {
          if (window.confirm('Replace the AIOStreams pairing link? The current link will stop working.')) void onUpdatePairing('PUT');
        }} className={primaryButton}>
          Replace pairing link
        </button>
        <button type="button" disabled={busy} onClick={() => {
          if (window.confirm('Disconnect AIOStreams?')) void onUpdatePairing('DELETE');
        }} className={disconnectButton}>
          Disconnect
        </button>
      </div>
      <form className="space-y-2 pb-3" onSubmit={event => {
        event.preventDefault();
        void onSaveInstance();
      }}>
        <label htmlFor="aiostreams-instance" className="block text-sm text-content">Playback instance URL</label>
        <div className="flex gap-2">
          <input id="aiostreams-instance" type="url" autoComplete="off" spellCheck={false} aria-describedby="aiostreams-playback-support"
            className="min-w-0 flex-1 border border-border-strong bg-surface px-3 text-sm text-content"
            value={instanceUrl} onChange={event => onInstanceUrlChange(event.target.value)} disabled={busy} />
          <button type="submit" className={primaryButton} disabled={busy || instanceUrl === savedInstanceUrl}>
            Save
          </button>
        </div>
        <p id="aiostreams-playback-support" className="text-sm text-content-muted">
          Direct playback requires an AIOStreams build with exact web launch support.
        </p>
      </form>
      {manifestUrl && (
        <div className="space-y-2 pb-3">
          <p className="text-sm text-content-muted">In AIOStreams, add this private URL under Addons → My Addons → Add by URL.</p>
          <div className="flex gap-2">
            <input aria-label="AIOStreams pairing URL" className="min-w-0 flex-1 border border-border-strong bg-surface px-3 text-sm text-content" readOnly value={manifestUrl} />
            <button type="button" className={primaryButton} disabled={busy} onClick={() => void onCopyManifestUrl()}>Copy URL</button>
          </div>
        </div>
      )}
    </>
  );
}

function PairingFeedback({ copied, error }: { copied: boolean; error: string }) {
  return (
    <>
      <div role="status" className="pointer-events-none fixed inset-x-4 bottom-5 z-50 flex justify-end sm:right-6">
        {copied && (
          <div className="flex items-center gap-2 border border-border-strong bg-surface-raised px-4 py-3 text-sm font-semibold text-content shadow-lg">
            <Check className="size-4 text-success-content" aria-hidden />
            Pairing URL copied
          </div>
        )}
      </div>
      {error && <p className="text-sm text-danger-content" role="alert">{error}</p>}
    </>
  );
}
