# Cantaro

This is a media and music library management application.

## Dev tools

We use dotnet for the backend and bun with Vite, React, TypeScript, Tailwind and Tanstack router for the frontend.

### Running the application

We use Aspire for development, use aspire start or stop to boot up the database, backend, and frontend. load the aspire skill if more info is needed on using aspire.
Aspire is using https and the test users credentials are in Cantaro.MigrationService\appsettings.Development.json

### Aspire publish output

The `aspire-output` folders are generated artifacts from `aspire publish` or `aspire deploy`. Do not hand-edit them as source; update the AppHost or deployment inputs and regenerate the output instead.

### Tests

If you run the dotnet tests, make sure that aspire is stopped as the processes otherwise hold a lock on files in the bin folder.

### Linting

For validating the frontend code, please use bun run check and before finishing use bun run check:fallow to find issues.
