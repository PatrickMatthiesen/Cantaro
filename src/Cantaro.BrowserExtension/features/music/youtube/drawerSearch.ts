export interface LyricsSearch { title: string; artist: string }

export function createLyricsSearchForm(doc: Document, submit: (query: LyricsSearch) => void) {
  const form = doc.createElement('form');
  form.className = 'search-form';
  form.hidden = true;
  form.addEventListener('keydown', event => {
    if (event.key !== 'Escape') event.stopPropagation();
  });
  const title = searchInput(doc, form, 'Song title');
  const artist = searchInput(doc, form, 'Artist (optional)');
  artist.required = false;
  artist.setAttribute('aria-label', 'Artist');
  const button = doc.createElement('button');
  button.type = 'submit';
  button.className = 'retry';
  button.textContent = 'Search LRCLIB';
  const actions = doc.createElement('div');
  actions.className = 'search-actions';
  actions.append(button);
  form.append(actions);
  form.addEventListener('submit', event => {
    event.preventDefault();
    const query = { title: title.value.trim(), artist: artist.value.trim() };
    if (query.title) submit(query);
  });
  return { form, title, artist, button };
}

function searchInput(doc: Document, form: HTMLFormElement, text: string): HTMLInputElement {
  const label = doc.createElement('label');
  label.textContent = text;
  const input = doc.createElement('input');
  input.type = 'text';
  input.required = true;
  input.maxLength = 200;
  input.autocomplete = 'off';
  input.spellcheck = false;
  input.setAttribute('enterkeyhint', 'search');
  input.setAttribute('aria-label', text);
  label.append(input);
  form.append(label);
  return input;
}
