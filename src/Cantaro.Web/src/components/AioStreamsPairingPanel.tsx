import { useEffect, useState } from 'react';
import { Check } from 'lucide-react';
import { ConnectionCard } from './ConnectionCard';

export const aioStreamsPairingEndpoint = '/api/media/aiostreams-pairing';
const endpoint = aioStreamsPairingEndpoint;
const buttonClass = 'inline-flex min-h-10 items-center justify-center px-4 text-xs font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:opacity-60';
const primaryButton = `${buttonClass} bg-personal-accent text-personal-accent-content enabled:cursor-pointer enabled:hover:bg-personal-accent-hover`;
const disconnectButton = `${buttonClass} border border-danger-border text-danger-content enabled:cursor-pointer enabled:hover:bg-danger-surface`;

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
    void (async () => {
      try {
        const response = await fetch(endpoint, { signal: controller.signal });
        if (!response.ok) throw new Error();
        const status = await response.json() as { paired: boolean; playbackInstanceUrl?: string };
        setPaired(status.paired);
        setInstanceUrl(status.playbackInstanceUrl ?? '');
        setSavedInstanceUrl(status.playbackInstanceUrl ?? '');
      } catch {
        if (!controller.signal.aborted) setError('Could not load the AIOStreams connection.');
      }
    })();
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
      const response = await fetch(endpoint, { method });
      if (!response.ok) throw new Error();
      if (method === 'PUT') {
        const pairing = await response.json() as { manifestPath: string };
        setManifestUrl(new URL(pairing.manifestPath, window.location.origin).href);
        setPaired(true);
        setShowManage(true);
      } else {
        setPaired(false);
        setManifestUrl('');
        setInstanceUrl('');
        setSavedInstanceUrl('');
        setShowManage(false);
      }
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
      const response = await fetch(`${endpoint}/playback`, {
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

  return (
    <div>
      <ConnectionCard
        name="AIOStreams"
        detail={savedInstanceUrl ? 'Playback and watch history' : paired ? 'Pairing link active' : 'Watch progress and history'}
        icon={<img src="https://aiostreams.org/logo.png" alt="" className="h-10 w-10 shrink-0 object-contain" />}
        implemented
        connected={paired === true}
        reconnect={false}
        busy={busy || (paired === null && !error)}
        actionLabel={paired ? 'Manage' : paired === null && error ? 'Retry' : undefined}
        onAction={() => {
          if (paired === null) {
            setError('');
            setStatusAttempt(attempt => attempt + 1);
          } else if (paired) {
            setShowManage(show => !show);
          } else {
            void update('PUT');
          }
        }}
      />
      {paired && showManage && (
        <div className="flex flex-wrap gap-2 pb-3">
          <button type="button" disabled={busy} onClick={() => {
            if (window.confirm('Replace the AIOStreams pairing link? The current link will stop working.')) void update('PUT');
          }} className={primaryButton}>
            Replace pairing link
          </button>
          <button type="button" disabled={busy} onClick={() => {
            if (window.confirm('Disconnect AIOStreams?')) void update('DELETE');
          }} className={disconnectButton}>
            Disconnect
          </button>
        </div>
      )}
      {paired && showManage && (
        <form className="space-y-2 pb-3" onSubmit={event => {
          event.preventDefault();
          void saveInstance();
        }}>
          <label htmlFor="aiostreams-instance" className="block text-sm text-content">Playback instance URL</label>
          <div className="flex gap-2">
            <input id="aiostreams-instance" type="url" autoComplete="off" spellCheck={false}
              className="min-w-0 flex-1 border border-border-strong bg-surface px-3 text-sm text-content"
              value={instanceUrl} onChange={event => setInstanceUrl(event.target.value)} disabled={busy} />
            <button type="submit" className={primaryButton} disabled={busy || instanceUrl === savedInstanceUrl}>
              Save
            </button>
          </div>
        </form>
      )}
      {showManage && manifestUrl && (
        <div className="space-y-2 pb-3">
          <p className="text-sm text-content-muted">In AIOStreams, add this private URL under Addons → My Addons → Add by URL.</p>
          <div className="flex gap-2">
            <input aria-label="AIOStreams pairing URL" className="min-w-0 flex-1 border border-border-strong bg-surface px-3 text-sm text-content" readOnly value={manifestUrl} />
            <button type="button" className={primaryButton} disabled={busy} onClick={async () => {
              setError('');
              try {
                await navigator.clipboard.writeText(manifestUrl);
                setCopiedAt(Date.now());
              } catch {
                setError('Select and copy the URL from the field.');
              }
            }}>Copy URL</button>
          </div>
        </div>
      )}
      <div role="status" className="pointer-events-none fixed inset-x-4 bottom-5 z-50 flex justify-end sm:right-6">
        {copiedAt !== null && (
          <div className="flex items-center gap-2 border border-border-strong bg-surface-raised px-4 py-3 text-sm font-semibold text-content shadow-lg">
            <Check className="h-4 w-4 text-success-content" aria-hidden />
            Pairing URL copied
          </div>
        )}
      </div>
      {error && <p className="text-sm text-danger-content" role="alert">{error}</p>}
    </div>
  );
}
