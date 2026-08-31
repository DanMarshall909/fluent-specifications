#!/usr/bin/env bash

# Builds, verifies, and publishes the site under gh-pages:/docs.
set -euo pipefail

npm test

git add . ':(exclude).astro/data-store.json' ':(exclude).astro/settings.json'
if [[ -n "$(git status --porcelain)" ]]; then
  git commit -m "Publish documentation $(date '+%Y-%m-%d %H:%M:%S')"
fi

git pull --rebase --autostash origin main
git push origin main

deploy_worktree="$(mktemp -d)"
git worktree add --detach "$deploy_worktree" origin/gh-pages
rm -rf "$deploy_worktree/docs"
mkdir -p "$deploy_worktree/docs"
cp -a docs/. "$deploy_worktree/docs/"
(
  cd "$deploy_worktree"
  git add -f -A docs
  if [[ -n "$(git status --porcelain)" ]]; then
    git commit -m "Deploy documentation $(date '+%Y-%m-%d %H:%M:%S')"
    git push origin HEAD:gh-pages --force
  fi
)
git worktree remove "$deploy_worktree"

echo 'Documentation published to https://fluent-spec.danmarshall.dev.'
