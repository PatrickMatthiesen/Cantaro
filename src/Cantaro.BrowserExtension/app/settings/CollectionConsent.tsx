import { useState } from 'react';
import { GradientButton } from '@cantaro/client-shared/ui';

export interface CollectionChoices {
  watchTracking: boolean;
  catalogCollection: boolean;
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
    || draft.catalogCollection !== props.choices.catalogCollection;
  const enabling = draft.watchTracking || draft.catalogCollection;
  return (
    <section className="space-y-4 py-5" aria-labelledby="collection-consent-title">
      <div>
        <h2 id="collection-consent-title" className="text-lg font-bold text-content">
          {props.needsReview ? 'Keep track of what you watch' : 'Website collection'}
        </h2>
        <p className="mt-2 text-sm leading-6 text-content-muted">
          Keep your library up to date as you watch on Crunchyroll. Choose what Cantaro can read
          from the page—nothing is collected until you sign in and allow it.
        </p>
      </div>
      <div className="divide-y divide-border-subtle border-y border-border-subtle">
        <ConsentChoice
          label="Automatically track what I watch"
          detail="Read episode information and playback progress to save watched episodes and sync with services I connect."
          checked={draft.watchTracking}
          disabled={props.busy}
          onChange={watchTracking => setDraft(current => ({ ...current, watchTracking }))}
        />
        <ConsentChoice
          label="Help Cantaro find episodes and watch links"
          detail="Read episode lists and links while I browse, even when I’m not watching, to improve Cantaro’s shared catalog."
          checked={draft.catalogCollection}
          disabled={props.busy}
          onChange={catalogCollection => setDraft(current => ({ ...current, catalogCollection }))}
        />
      </div>
      <section aria-labelledby="collection-information-title" className="space-y-2 text-xs leading-5 text-content-muted">
        <h3 id="collection-information-title" className="font-semibold text-content">Where your information goes</h3>
        <p>To your Cantaro server: <strong className="break-all font-medium text-content">{props.baseUrl}</strong>.</p>
        <ul className="list-disc space-y-1 pl-4">
          <li>Watch progress stays in your library. Temporary matching details are deleted once processed; unfinished matches stay until you resolve or dismiss them.</li>
          <li>Shared episode and watch-link facts can remain after account deletion, without keeping your browsing history.</li>
        </ul>
        <p>You can turn either feature off anytime. This stops new collection; it does not delete information already saved.</p>
      </section>
      <p className="text-xs leading-5 text-content-muted">
        Music features do not read music websites.
        {' '}<a href="https://github.com/PatrickMatthiesen/Cantaro/blob/main/PRIVACY.md" target="_blank" rel="noreferrer"
          className="font-semibold text-personal-accent-strong underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-focus">Read the privacy policy</a>.
      </p>
      {props.error ? <p role="alert" className="text-sm text-content">{props.error}</p> : null}
      <ConsentActions {...props} draft={draft} changed={changed} enabling={enabling} />
    </section>
  );
}

function ConsentActions(props: CollectionConsentProps & { draft: CollectionChoices; changed: boolean; enabling: boolean }) {
  return (
      <div className="flex flex-wrap items-center gap-3">
        {!props.authenticated ? (
          <GradientButton type="button" disabled={props.busy} onClick={() => void props.onSignIn()}>Sign in to enable collection</GradientButton>
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
    <label className="flex cursor-pointer items-start gap-3 py-4 focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-focus">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={event => onChange(event.target.checked)}
        className="mt-1 size-4 shrink-0 accent-personal-accent" />
      <span>
        <span className="block text-sm font-semibold text-content">{label}</span>
        <span className="mt-1 block text-xs leading-5 text-content-muted">{detail}</span>
      </span>
    </label>
  );
}
