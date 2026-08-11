# Firefox source review

The Firefox release source archive contains the Cantaro browser extension, its
workspace dependency, and the root Bun lockfile. It does not require generated
files or private packages.

Build the submitted Firefox extension from the root of the extracted archive:

```sh
bun install --frozen-lockfile
cd src/Cantaro.BrowserExtension
WEB_HTTP=https://cantaro.bmstack.net bun run build:firefox
```

The generated extension is written to
`src/Cantaro.BrowserExtension/.output/firefox-mv2/`. The release workflow builds
with the same Bun version, lockfile, command, and `WEB_HTTP` value before
creating the submitted Firefox ZIP.
