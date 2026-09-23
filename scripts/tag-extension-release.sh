#!/usr/bin/env bash
set -euo pipefail

: "${EXTENSION_VERSION:?EXTENSION_VERSION is required}"
: "${TARGET_SHA:?TARGET_SHA is required}"

TAG="extension/v$EXTENSION_VERSION"

if git show-ref --verify --quiet "refs/tags/$TAG"; then
  EXISTING_SHA=$(git rev-parse "$TAG^{commit}")
  if [ "$EXISTING_SHA" != "$TARGET_SHA" ]; then
    echo "::error::Tag $TAG already points to $EXISTING_SHA; refusing to move it to $TARGET_SHA."
    exit 1
  fi

  echo "Tag $TAG already points to $TARGET_SHA."
  exit 0
fi

stores=()
[ "${CHROME_SUBMITTED:-false}" = "true" ] && stores+=("Chrome")
[ "${FIREFOX_SUBMITTED:-false}" = "true" ] && stores+=("Firefox")
if [ "${#stores[@]}" -eq 0 ]; then
  echo "::error::At least one confirmed store submission is required before tagging."
  exit 1
fi
store_list=$(IFS=,; echo "${stores[*]}")

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git tag -a "$TAG" "$TARGET_SHA" -m "Extension version $EXTENSION_VERSION submitted to $store_list"
git push origin "$TAG"
echo "Created and pushed tag $TAG for $TARGET_SHA."
