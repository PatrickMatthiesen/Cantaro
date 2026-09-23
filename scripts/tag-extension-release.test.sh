#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
TAG_SCRIPT="$SCRIPT_DIR/tag-extension-release.sh"
TEST_ROOT=$(mktemp -d)
trap 'rm -rf "$TEST_ROOT"' EXIT

git init --bare --quiet "$TEST_ROOT/remote.git"
git init --quiet "$TEST_ROOT/repository"
cd "$TEST_ROOT/repository"
git config user.name "Release Test"
git config user.email "release-test@example.test"
git remote add origin "$TEST_ROOT/remote.git"

echo first > release.txt
git add release.txt
git commit --quiet -m first
FIRST_SHA=$(git rev-parse HEAD)

export EXTENSION_VERSION=9.8.7
export TARGET_SHA=$FIRST_SHA
export CHROME_SUBMITTED=true
export FIREFOX_SUBMITTED=false

# A missing tag must be created and pushed.
bash "$TAG_SCRIPT"
REMOTE_SHA=$(git --git-dir="$TEST_ROOT/remote.git" rev-parse "extension/v9.8.7^{commit}")
test "$REMOTE_SHA" = "$FIRST_SHA"

# Re-running for the same commit must be idempotent.
bash "$TAG_SCRIPT" | grep -F "already points to $FIRST_SHA"

# An existing tag must never move to a different commit.
echo second >> release.txt
git commit --quiet -am second
SECOND_SHA=$(git rev-parse HEAD)
export TARGET_SHA=$SECOND_SHA
if bash "$TAG_SCRIPT" > "$TEST_ROOT/different-commit.log" 2>&1; then
  echo "Expected different-commit tag creation to fail." >&2
  exit 1
fi
grep -F "refusing to move it to $SECOND_SHA" "$TEST_ROOT/different-commit.log"
REMOTE_SHA=$(git --git-dir="$TEST_ROOT/remote.git" rev-parse "extension/v9.8.7^{commit}")
test "$REMOTE_SHA" = "$FIRST_SHA"

echo "Extension release tag tests passed."
