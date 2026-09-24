# Cantaro Browser Extension Privacy Policy

Effective September 24, 2026

Cantaro reads supported streaming pages to provide opted-in media tracking and YouTube lyrics. Cantaro does not sell user data or use it for advertising.

## Scope

This policy describes how the Cantaro browser extension handles data. The
extension asks for affirmative, informed consent before it reads a supported
streaming page for media collection. Collection is off by default.

Cantaro can connect to a hosted Cantaro service or to a self-hosted instance chosen by the user. The operator of the configured server controls the data stored there. When a user self-hosts Cantaro, that user or their chosen administrator controls the server and its retention, logging, and security practices.

## Data Cantaro handles

Depending on the features used, the extension handles the following data:

- **Account and authentication data:** The user's Cantaro account email, configured server address, and access and refresh tokens used to keep the extension signed in. Cantaro does not receive the user's password through the extension.
- **Settings and temporary state:** Page feature preferences, versioned consent choices, diagnostic logging preference, tracking pauses, and popup state.
- **Crunchyroll activity and content:** Only after the user has opted in to the relevant media feature and is signed in, the extension can read the rendered page URL, provider identifiers, series, season, and episode titles, episode numbers, available audio or subtitle languages, playback position, duration, watch progress, observation time, and extension version. Catalog observations are collected from rendered series pages when catalog collection is enabled. Watch-progress observations are submitted when playback reaches Cantaro's completion threshold and watch tracking is enabled.
- **Music library and playlists:** These features use authenticated Cantaro account and popup requests. They do not read music websites or collect music-page browsing activity. When the user opens a song's lyrics in the extension, it sends the song's title and artist directly to LRCLIB over HTTPS. The song's album and duration stay in the extension and help rank results.
- **YouTube lyrics:** Only after the user enables this separate option and is signed in, the extension reads the YouTube video ID from the active video page and checks it against the user's existing Cantaro music library. The video ID stays within the extension. For a match, the extension sends the song's title and artist directly to LRCLIB over HTTPS to find lyrics. Album and duration stay in the extension and help rank results. It does not send the Cantaro song ID to LRCLIB. If there is no match, Cantaro reports that the video is not linked to a library song. It does not automatically match songs or change the library. The page title may prefill manual search fields locally. Suggested terms are sent to LRCLIB only when the user submits the search. The extension does not send ad text, full URLs, or browsing history. The lyrics feature does not read playback timing.
- **Standard server request data:** The configured server and its hosting provider may process IP addresses, request timestamps, browser or user-agent details, and requested endpoints in operational or security logs. The extension does not separately collect precise location.

The extension does not collect health, financial, payment, or personal communication data. It does not include third-party advertising or analytics SDKs.

## How data is used

Cantaro uses this data only to:

- authenticate the user with the selected Cantaro server;
- show the user's Cantaro music library and playlists;
- find an existing library song for an opted-in YouTube video and request lyrics from LRCLIB using that song's title and artist, then rank results locally with album and duration;
- request lyrics from LRCLIB when the user opens a library song's lyrics in the extension;
- record supported media catalog information and watch progress in the user's library when the user has enabled those separate collection purposes;
- apply the user's settings and provide diagnostics the user explicitly enables; and
- maintain, secure, and troubleshoot those user-facing features.

## Storage and transfers

### In the browser

The extension stores settings, the versioned consent choices, the refresh credential used to preserve sign-in, the signed-in email address, tracking preferences, and popup state in Chrome local extension storage. Local storage is restricted to trusted extension contexts so content scripts cannot read it. Short-lived access credentials are stored in Chrome session storage and are refreshed after a browser restart without requiring the user to sign in again while the refresh session remains valid. Before consent, when a feature is disabled, or while signed out, supported-page content is not collected, queued, or transmitted. Failed Crunchyroll catalog or watch observations are not retained for later delivery. Diagnostic messages, which may include supported-page URLs, titles, provider identifiers, and progress details, are written to the browser console. Cantaro does not send them to a separate analytics service.

### On the configured Cantaro server

The extension sends account requests and delivered Crunchyroll media observations over HTTPS to the Cantaro server configured by the user. Observation URLs have query strings and fragments removed before delivery and again at server ingestion. The server turns each report into the structured facts needed for matching and library features. It does not retain full serialized observation requests or playback position, duration and percentage in observation records. Duplicate title values are not stored in a second request body. Once a report has been processed successfully, its temporary matching evidence is removed. If Cantaro cannot identify the episode automatically, the evidence is kept as a private matching task until it is resolved or dismissed.

When a user connects an external music or media provider, the configured server may exchange the identifiers, library state, playlists, or progress needed to perform the action requested by the user. Those services process data under their own privacy terms. For YouTube lyrics, the extension keeps the video ID local and checks it against the user's Cantaro library. It sends the matched song's title and artist directly to LRCLIB for the lyrics search. The extension uses the song's album and duration to rank results locally. Opening lyrics for a library song in the extension sends the same title and artist to LRCLIB. The extension does not send supported-page observations to advertising networks or data brokers.

## Consent and user controls

The YouTube lyrics drawer also supports manual search. When the user submits
a song title and optional artist, the extension sends those entered terms directly to
LRCLIB. Manual searches do not require a Cantaro library entry, import songs,
or change library identities. Search results remain in the current drawer. The page title may supply editable search suggestions, which stay local until the user submits them.

On first use, the extension shows a consent screen before a supported page is
read for media collection. It explains the information read, the purpose of
each collection feature, the configured server that receives it, and links to
this policy. Collection is disabled until the user affirmatively saves a
choice. Media watch tracking, catalog collection, and YouTube lyrics have
separate controls. The YouTube lyrics option requires a signed-in account. The
extension reads the active video's ID and checks for an existing song in the
user's Cantaro library. For a match, it sends the song's title and artist
directly to LRCLIB for a lyrics search. The extension uses the album and
duration to rank results locally. Opening lyrics for a library song in the
extension sends the same title and artist to LRCLIB. If there is no match, Cantaro reports
that the video is not linked to a library song and leaves the library unchanged. The
extension does not automatically transmit YouTube page titles, ad text, full URLs, or browsing
history. The lyrics feature does not read playback timing.

Consent is stored locally with a policy version and the user's choices. A
material change to what a feature reads or sends raises the required version
and pauses that feature until the user reviews and accepts the new disclosure.
The user can revoke or reset a choice from the extension settings. Revocation
immediately stops new collection for that purpose and clears pending local
delivery state. Signing out also stops media collection and YouTube lyrics
requests; signing in again does not re-enable them until the user is
authenticated and has an accepted consent choice.

## Browser permissions

Cantaro requests these browser capabilities for its disclosed features:

- **`storage`:** Stores settings, authentication tokens, and temporary preferences on the user's device.
- **`identity`:** Runs the browser-managed Authorization Code with PKCE sign-in flow against the configured Cantaro server.
- **Site access:** Lets Cantaro read rendered Crunchyroll pages for opted-in media collection and the active YouTube video ID for opted-in lyrics. The lyrics feature also contacts LRCLIB directly with matched song titles and artist names. Broad HTTPS access is optional and is requested only for the exact self-hosted Cantaro origin configured by the user.

## Sharing and Chrome Web Store Limited Use

Cantaro does not sell user data. Cantaro does not use or transfer user data for personalized advertising, retargeting, interest-based advertising, creditworthiness, lending, or unrelated profiling.

Data is transferred only when necessary to provide or improve Cantaro's disclosed features, to the configured server and connected providers chosen by the user, to service providers acting on the server operator's behalf, for security or legal compliance, or as part of a merger or acquisition with the consent required by applicable policy and law.

People are not permitted to read user data except with the user's explicit consent for support, when necessary for security, when required by law, or when data has been aggregated and anonymized for permitted internal operations.

Cantaro's use of information received from Google APIs adheres to the [Chrome Web Store User Data Policy](https://developer.chrome.com/docs/webstore/program-policies/limited-use/), including the Limited Use requirements. Cantaro's access to and use of Google user data is also limited to the practices disclosed in this policy in accordance with the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy).

## Retention and user controls

- Signing out removes the extension authentication session from local storage and stops media collection. A later sign-in does not re-enable media collection without an accepted consent choice.
- The configured server retains account and library data while the account is active. Temporary media evidence is removed after successful processing. Evidence for an unresolved match is kept only in that user's private matching task until the task is resolved or dismissed. Processing temporary evidence does not erase library watch progress.
- Cantaro's web settings let users export their retained user-scoped data, disconnect providers, and permanently delete their account and user-scoped live records, including unresolved matching tasks and dependent episode evidence.
- Shared canonical titles, episodes, provider mappings, playable episode identities and language availability remain until explicitly maintained or removed, including after the source report is processed or an account is deleted. These are specific catalog facts without a user owner; they do not retain the contributing user's page visits, watch time, or browsing history.
- Server backups and operational or security logs follow the configured server operator's retention rules. Cantaro does not impose one retention period on every self-hosted deployment.

## Security

Production and runtime-configured Cantaro connections use HTTPS. The extension uses browser-managed identity APIs and PKCE for sign-in, limits persistent authentication storage to the refresh credential needed to preserve sign-in, keeps short-lived access credentials in browser session storage, restricts local storage to trusted extension contexts, and requests only the provider and configured-server access needed for its features.

No method of storage or transmission is completely secure. Users should keep Chrome and their Cantaro server up to date and protect access to their browser profile.

## Changes to this policy

This policy will be updated when Cantaro's data practices materially change. The effective date at the top identifies the latest revision.

## Contact

Cantaro is maintained as an open-source project. For privacy questions or concerns, contact the maintainer through the [Cantaro issue tracker](https://github.com/PatrickMatthiesen/Cantaro/issues).

Issues are public. Do not include passwords, tokens, private URLs, or other sensitive information. Account data can be exported or deleted directly from Cantaro's **Data and privacy** settings.
