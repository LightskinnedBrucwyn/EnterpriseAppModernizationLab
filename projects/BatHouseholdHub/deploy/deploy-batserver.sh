#!/usr/bin/env bash
set -euo pipefail

cd "$HOME/bat-household-hub"
git pull
cd projects/BatHouseholdHub
docker compose up -d --build
docker compose ps

for i in $(seq 1 15); do
    if curl --fail --silent --show-error http://127.0.0.1:5188/bills >/dev/null 2>&1; then
        echo "BAT_HOUSEHOLD_DEPLOY_OK"
        exit 0
    fi
    sleep 2
done

echo "BAT_HOUSEHOLD_DEPLOY_FAILED: app didn't respond on /bills after 30s" >&2
exit 1
