# Chrome Web Store data disclosure

This file is the source of truth for the values to enter in the Chrome Web
Store Developer Dashboard for the Cantaro extension. The dashboard is an
external system; editing this file does not update or publish the listing.

## Listing copy

Use the following single purpose description:

> Cantaro tracks watched episodes on supported streaming pages and provides a
> signed-in interface for the user's Cantaro media and music library.

Use the public URL of the deployed [Cantaro Privacy Policy](../PRIVACY.md) in
the dashboard. The repository policy is the source text, but a public HTTPS
URL must be available before submitting the listing. The extension links to
`https://github.com/PatrickMatthiesen/Cantaro/blob/main/PRIVACY.md`; verify that
the policy has reached `main` before publishing the extension.

## Data handling declarations

Declare the data the extension can handle when its features are enabled, even
though collection requires opt-in. Review the current dashboard wording against
Chrome's [User Data FAQ](https://developer.chrome.com/docs/webstore/program-policies/user-data-faq).

| Chrome Web Store category | Data handled by Cantaro | Purpose and destination |
| --- | --- | --- |
| Authentication information | Cantaro account email and extension access/refresh credentials | Keep the user signed in to the configured Cantaro server. The extension does not receive the account password. |
| Web history / browsing activity | Supported Crunchyroll page URL, provider identifiers, rendered series/season/episode information, playback progress, and observation time | With separate consent for watch tracking or catalog collection, send structured media evidence over HTTPS to the configured Cantaro server. URLs are stripped of query strings and fragments. |
| Website content | Rendered Crunchyroll titles, episode numbers, language labels, and episode links | Match the user's media to library and episode records on the configured Cantaro server after the corresponding catalog or watch consent is accepted. |
| Personally identifiable information | Account email, only as part of the Cantaro account session | Display account state and associate the user's private library data with the account. |
| User activity | Watch progress and, when the user requests music actions, library or playlist identifiers | Update the user's Cantaro library and perform the requested sync action on the configured server. |

Do not declare music website browsing or music website content. Cantaro's music
features are signed-in popup and account operations for library, playlist, and
lyrics actions; the extension does not scrape Spotify, YouTube, or other music
websites.

## Required answers

- **Sell user data:** No.
- **Use data for advertising:** No.
- **Use data for creditworthiness, lending, or unrelated profiling:** No.
- **Transfer data for a purpose unrelated to the extension's disclosed feature:** No.
- **Data is handled only transiently:** No for authenticated media observations; structured evidence may be retained by the configured server under the retention policy.
- **Privacy policy:** Yes. Link to the deployed public HTTPS policy URL.

The extension displays a first-use consent screen before reading a supported
page for media collection. Collection is off by default. Watch tracking and
catalog collection are separate choices. A signed-out user produces no media
capture, queue entry, or transmission. Revocation stops new collection for the
purpose immediately, and a material change to collection requires renewed,
versioned consent.

## Permission rationale

Use the matching explanations from
[`PERMISSIONS.md`](../src/Cantaro.BrowserExtension/PERMISSIONS.md):

- `storage` stores settings, local consent choices, and the extension session.
- `identity` runs the browser-managed OAuth/PKCE sign-in flow.
- `scripting` refreshes an already-open Cantaro page after a progress update.
- `www.crunchyroll.com` is read only for rendered media pages after consent and authentication.
- The optional HTTPS origin is requested only for the exact Cantaro server origin entered by the user.

## Publication checklist

- [ ] Deploy the policy at a stable public HTTPS URL.
- [ ] Verify the URL in an incognito browser without repository authentication.
- [ ] Enter the declarations and answers above in the Chrome Web Store dashboard.
- [ ] Verify the dashboard disclosures match the shipped consent version and enabled host permissions.
- [ ] Record the dashboard review or publication result in the release notes.

