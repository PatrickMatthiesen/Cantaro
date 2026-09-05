# Cantaro Browser Extension Privacy Policy

Effective September 5, 2026

Cantaro observes supported streaming pages only to provide its media-library and watch-tracking features. Cantaro does not sell user data or use it for advertising.

## Scope

This policy describes how the Cantaro browser extension handles data.

Cantaro can connect to a hosted Cantaro service or to a self-hosted instance chosen by the user. The operator of the configured server controls the data stored there. When a user self-hosts Cantaro, that user or their chosen administrator controls the server and its retention, logging, and security practices.

## Data Cantaro handles

Depending on the features used, the extension handles the following data:

- **Account and authentication data:** The user's Cantaro account email, configured server address, and access and refresh tokens used to keep the extension signed in. Cantaro does not receive the user's password through the extension.
- **Settings and temporary state:** Page feature preferences, diagnostic logging preference, tracking pauses, and popup state.
- **Crunchyroll activity and content:** Page URLs, provider identifiers, rendered series, season, and episode titles, episode numbers, available audio or subtitle languages, playback position, duration, watch progress, observation time, and extension version. Catalog observations are collected from rendered series pages. Watch-progress observations are submitted when playback reaches Cantaro's completion threshold.
- **Standard server request data:** The configured server and its hosting provider may process IP addresses, request timestamps, browser or user-agent details, and requested endpoints in operational or security logs. The extension does not separately collect precise location.

The extension does not collect health, financial, payment, or personal communication data. It does not include third-party advertising or analytics SDKs.

## How data is used

Cantaro uses this data only to:

- authenticate the user with the selected Cantaro server;
- show the user's Cantaro music library, playlists, and lyrics information;
- record supported media catalog information and watch progress in the user's library;
- apply the user's settings and provide diagnostics the user explicitly enables; and
- maintain, secure, and troubleshoot those user-facing features.

## Storage and transfers

### In the browser

The extension stores settings, the refresh credential used to preserve sign-in, the signed-in email address, tracking preferences, and popup state in Chrome local extension storage. Local storage is restricted to trusted extension contexts so content scripts cannot read it. Short-lived access credentials are stored in Chrome session storage and are refreshed after a browser restart without requiring the user to sign in again while the refresh session remains valid. Failed Crunchyroll catalog or watch observations are not retained for later delivery. Diagnostic messages, which may include supported-page URLs, titles, provider identifiers, and progress details, are written to the browser console. Cantaro does not send them to a separate analytics service.

### On the configured Cantaro server

The extension sends account requests and delivered Crunchyroll media observations over HTTPS to the Cantaro server configured by the user. Observation URLs have query strings and fragments removed before delivery and again at server ingestion. The server retains structured title, episode, provider, language, progress and resolution information needed for matching and library features. It does not retain full serialized observation requests or playback position, duration and percentage in observation records. Duplicate title values are not stored in a second request body.

When a user connects an external music or media provider, the configured server may exchange the identifiers, library state, playlists, or progress needed to perform the action requested by the user. Those services process data under their own privacy terms. The extension does not send supported-page observations to advertising networks or data brokers.

## Browser permissions

Cantaro requests these browser capabilities for its disclosed features:

- **`storage`:** Stores settings, authentication tokens, and temporary preferences on the user's device.
- **`identity`:** Runs the browser-managed Authorization Code with PKCE sign-in flow against the configured Cantaro server.
- **`scripting`:** Notifies an already-open Cantaro web page after the extension updates media progress, allowing the page to refresh the user's visible library state.
- **Site access:** Lets Cantaro read the rendered page on supported Crunchyroll pages. Broad HTTPS access is optional and is requested only for the exact self-hosted Cantaro origin configured by the user.

## Sharing and Chrome Web Store Limited Use

Cantaro does not sell user data. Cantaro does not use or transfer user data for personalized advertising, retargeting, interest-based advertising, creditworthiness, lending, or unrelated profiling.

Data is transferred only when necessary to provide or improve Cantaro's disclosed features, to the configured server and connected providers chosen by the user, to service providers acting on the server operator's behalf, for security or legal compliance, or as part of a merger or acquisition with the consent required by applicable policy and law.

People are not permitted to read user data except with the user's explicit consent for support, when necessary for security, when required by law, or when data has been aggregated and anonymized for permitted internal operations.

Cantaro's use of information received from Google APIs adheres to the [Chrome Web Store User Data Policy](https://developer.chrome.com/docs/webstore/program-policies/limited-use/), including the Limited Use requirements. Cantaro's access to and use of Google user data is also limited to the practices disclosed in this policy in accordance with the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy).

## Retention and user controls

- Signing out removes the extension authentication session from local storage.
- The configured server retains account and library data while the account is active. Media observations, their episode evidence, matching candidates and resolution history expire after 30 days without a server-side update; cleanup runs at startup and hourly while the server is running. Observation expiry does not erase library watch progress.
- Cantaro's web settings let users export their retained user-scoped data, disconnect providers, and permanently delete their account and user-scoped live records, including observations and dependent episode evidence.
- Shared canonical titles, episodes, provider mappings, playable episode identities and language availability remain until explicitly maintained or removed, including after observation expiry or account deletion. These records contain catalog information without a user owner; they are not retained as the deleted user's viewing history.
- Server backups and operational or security logs follow the configured server operator's retention rules. Cantaro does not impose one retention period on every self-hosted deployment.

## Security

Production and runtime-configured Cantaro connections use HTTPS. The extension uses browser-managed identity APIs and PKCE for sign-in, limits persistent authentication storage to the refresh credential needed to preserve sign-in, keeps short-lived access credentials in browser session storage, restricts local storage to trusted extension contexts, and requests only the provider and configured-server access needed for its features.

No method of storage or transmission is completely secure. Users should keep Chrome and their Cantaro server up to date and protect access to their browser profile.

## Changes to this policy

This policy will be updated when Cantaro's data practices materially change. The effective date at the top identifies the latest revision.

## Contact

Cantaro is maintained as an open-source project. For privacy questions or concerns, contact the maintainer through the [Cantaro issue tracker](https://github.com/PatrickMatthiesen/Cantaro/issues).

Issues are public. Do not include passwords, tokens, private URLs, or other sensitive information. Account data can be exported or deleted directly from Cantaro's **Data and privacy** settings.
