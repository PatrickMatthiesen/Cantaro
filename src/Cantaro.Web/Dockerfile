FROM oven/bun:1 AS build

WORKDIR /repo

COPY package.json bun.lock ./
COPY src/Cantaro.ClientShared/package.json src/Cantaro.ClientShared/package.json
COPY src/Cantaro.Web/package.json src/Cantaro.Web/package.json
COPY src/Cantaro.BrowserExtension/package.json src/Cantaro.BrowserExtension/package.json
COPY src/Cantaro.ClientShared src/Cantaro.ClientShared
COPY src/Cantaro.Web src/Cantaro.Web
COPY src/Cantaro.BrowserExtension src/Cantaro.BrowserExtension

RUN --mount=type=cache,target=/root/.bun/install/cache bun install --frozen-lockfile

RUN cd src/Cantaro.Web && bun run build

FROM scratch

COPY --from=build /repo/src/Cantaro.Web/dist /app/dist
