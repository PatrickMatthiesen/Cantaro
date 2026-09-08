/** Removes query and fragment data from URLs retained with an observation. */
export function stripUrlQueryAndFragment(value: string): string {
  try {
    const url = new URL(value);
    url.search = '';
    url.hash = '';
    url.username = '';
    url.password = '';
    return url.href;
  } catch {
    // Keep malformed values usable for diagnostics while still removing anything
    // after a query or fragment delimiter.
    return value.split(/[?#]/, 1)[0] ?? value;
  }
}
