import { useState } from 'react';
import { GradientButton } from '@cantaro/client-shared/ui';

export interface CollectionChoices {
  watchTracking: boolean;
  catalogCollection: boolean;
  musicLyrics: boolean;
}

export interface CollectionConsentProps {
  choices: CollectionChoices;
  needsReview: boolean;
  authenticated: boolean;
  baseUrl: string;
  busy: boolean;
  error: string | null;
  onSave: (choices: CollectionChoices) => Promise<void>;
  onRevoke: () => Promise<void>;
  onReset: () => Promise<void>;
  onSignIn: () => Promise<boolean>;
}

export function CollectionConsent(props: CollectionConsentProps) {
  const [draft, setDraft] = useState(props.choices);
  const changed = draft.watchTracking !== props.choices.watchTracking
    || draft.catalogCollection !== props.choices.catalogCollection
    || draft.musicLyrics !== props.choices.musicLyrics;
  const enabling = draft.watchTracking || draft.catalogCollection || draft.musicLyrics;
  return (
    <section className="space-y-2.5 py-2" aria-labelledby="collection-consent-title">
      <div>
        <h2 id="collection-consent-title" className="text-lg font-bold text-content">
          {props.needsReview ? 'Choose website features' : 'Website collection'}
        </h2>
        <p className="mt-1 text-sm leading-5 text-content-muted">
          Cantaro reads pages only after you sign in and opt in.
        </p>
      </div>
      <div className="divide-y divide-border-subtle border-y border-border-subtle">
        <ConsentChoice
          label="Track what I watch"
          detail="Read Crunchyroll episode details and playback progress to update your library and connected services."
          checked={draft.watchTracking}
          disabled={props.busy}
          onChange={watchTracking => setDraft(current => ({ ...current, watchTracking }))}
        />
        <ConsentChoice
          label="Improve the shared episode catalog"
          detail="Read Crunchyroll episode lists and links as you browse."
          checked={draft.catalogCollection}
          disabled={props.busy}
          onChange={catalogCollection => setDraft(current => ({ ...current, catalogCollection }))}
        />
        <ConsentChoice
          label="YouTube lyrics"
          detail="Find lyrics on LRCLIB for YouTube songs in your library."
          checked={draft.musicLyrics}
          disabled={props.busy}
          onChange={musicLyrics => setDraft(current => ({ ...current, musicLyrics }))}
        />
      </div>
      <div className="space-y-1.5 text-xs leading-4 text-content-muted">
        <p>Watch and catalog data go to <strong className="break-all font-medium text-content">{props.baseUrl}</strong>.</p>
        <p>Watch progress stays in your library. Matching details are deleted when resolved or dismissed.
          Shared episode data can stay after account deletion.</p>
        <p>Turning a feature off stops new collection. Saved data stays.</p>
        <p>Library lyrics send titles and artists to LRCLIB even with YouTube lyrics off.
          {' '}<a href="https://github.com/PatrickMatthiesen/Cantaro/blob/main/PRIVACY.md" target="_blank" rel="noreferrer"
            className="font-semibold text-personal-accent-strong underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-focus">Privacy policy</a>
        </p>
      </div>
      {props.error ? <p role="alert" className="text-sm text-content">{props.error}</p> : null}
      <ConsentActions {...props} draft={draft} changed={changed} enabling={enabling} />
    </section>
  );
}

function ConsentActions(props: CollectionConsentProps & { draft: CollectionChoices; changed: boolean; enabling: boolean }) {
  const signInAndSave = async () => {
    const choices = { ...props.draft };
    if (await props.onSignIn()) await props.onSave(choices);
  };

  return (
      <div className="flex flex-wrap items-center gap-3">
        {!props.authenticated ? (
          <GradientButton type="button" disabled={props.busy} onClick={() => void signInAndSave()}>Sign in to enable collection</GradientButton>
        ) : (
          <GradientButton type="button" disabled={props.busy || (!props.needsReview && !props.changed)} onClick={() => void props.onSave(props.draft)}>
            {props.busy ? 'Saving…' : props.enabling ? 'Allow selected collection' : 'Keep collection off'}
          </GradientButton>
        )}
        <GradientButton type="button" tone="soft" disabled={props.busy} onClick={() => void props.onRevoke()}>
          {props.needsReview ? 'Continue without collection' : 'Turn off all collection'}
        </GradientButton>
        {!props.needsReview ? <button type="button" disabled={props.busy} onClick={() => void props.onReset()}
          className="min-h-11 px-2 text-sm text-content-muted underline underline-offset-4 hover:text-content focus-visible:outline-2 focus-visible:outline-focus">Reset choices</button> : null}
      </div>
  );
}

function ConsentChoice({ label, detail, checked, disabled, onChange }: {
  label: string;
  detail: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-start gap-3 py-2 focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-focus">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={event => onChange(event.target.checked)}
        className="mt-1 size-4 shrink-0 accent-personal-accent" />
      <span>
        <span className="block text-sm font-semibold text-content">{label}</span>
        <span className="mt-0.5 block text-xs leading-4 text-content-muted">{detail}</span>
      </span>
    </label>
  );
}
