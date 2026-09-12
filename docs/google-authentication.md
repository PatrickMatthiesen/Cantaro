# Google authentication

Cantaro supports three authentication modes through the `Authentication` configuration section:

- `Local` keeps password authentication enabled. This is the default.
- `GoogleOnly` disables local register, login, password recovery, email confirmation, password changes, and local account management endpoints. Google sign-in and extension token flows remain available.
- `Both` keeps both providers available during a migration.

Set the mode with `Authentication:Mode`. Google mode also requires both `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret`. The application validates these settings at startup when Google is enabled. Aspire exposes one shared credential pair, `GoogleClientId` and `GoogleClientSecret`, for both YouTube and Google sign-in. Use these shared names for local user secrets. Set `AuthenticationMode` to `Both` to test linking.

The deployment workflow uses `CANTARO_AUTHENTICATION_MODE` and the secrets `CANTARO_GOOGLE_CLIENT_ID` and `CANTARO_GOOGLE_CLIENT_SECRET`. Before deploying, save the production OAuth credentials under these Google secret names in the GitHub Production environment. The old YouTube secret names are no longer used. Aspire passes the shared pair to both backend configuration sections (`YouTube` and `Authentication:Google`). If running the API without Aspire, supply the same pair in both sections.

The Google OAuth redirect URI is the public application origin followed by `/signin-google`, unless `Authentication:Google:CallbackPath` changes it. The Google handler receives that callback and completes the Cantaro flow at `/api/auth/google/callback`. Register the handler callback URI with Google. Do not register or expose the completion endpoint as the Google provider callback.

The frontend can discover the active providers with `GET /api/auth/methods`:

```json
{
  "localLoginEnabled": true,
  "googleEnabled": false
}
```

`GET /api/auth/google/login?returnUrl=/...` starts Google sign-in. Return URLs must be local application paths. The server rejects absolute URLs, protocol-relative URLs, backslash-prefixed paths, control characters, and oversized values.

An existing password account must explicitly link Google. The client posts the current password to `POST /api/auth/google/link`:

```json
{
  "currentPassword": "...",
  "returnUrl": "/settings"
}
```

The response contains an `authorizationUrl` for the one-time Google challenge. Cantaro never merges an account solely because the Google email matches. It identifies the Google account by its stable provider subject (`sub`) and requires a verified Google email. If another Cantaro account owns the email, the callback returns a migration error that directs the user to sign in to that account and link Google there.

`GET /api/auth/google/status` reports the current account state:

```json
{
  "linked": true,
  "hasPassword": true,
  "reauthenticated": false
}
```

Passwordless Google accounts cannot attach an arbitrary second Google identity. `POST /api/auth/google/reauth` starts a fresh challenge with their already-linked identity. The challenge grant is single-use; its successful callback creates a signed session claim valid for five minutes. Google-based account deletion requires that recent claim and a separate confirmation request.

## Migrating an existing deployment

1. Reuse the existing YouTube Google web OAuth client and add `https://your-cantaro-host/signin-google` to its redirect URIs. Keep the existing YouTube callback URI. For local testing, also add the local web app origin followed by `/signin-google`.
2. Configure credentials and deploy with mode `Both`.
3. Each existing user signs in with their Cantaro password and links Google in Settings. Verify Google sign-in opens the same library before changing modes.
4. Switch to `GoogleOnly` after the accounts that need access have linked Google. An unlinked account cannot use its password in this mode. Temporarily return to `Both` if another existing user needs to migrate.

The current replay cache and request rate limits are per API process. Run a single API instance. Before scaling to multiple replicas, move replay protection and request budgets into shared storage; session affinity alone is not a security boundary. Configure the immediate trusted proxy IP before deploying behind a tunnel so per-IP limits use verified forwarded client addresses.

The Google handler maps both the current OpenID Connect `email_verified` field and the legacy Google user-info `verified_email` field to the verification claim used by Cantaro. The provider subject remains the account key; email is used for validation and collision handling only.

References:

- [ASP.NET Core Google authentication options](https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Google/src/GoogleOptions.cs)
- [Google OpenID Connect user info reference](https://developers.google.com/identity/openid-connect/reference)
