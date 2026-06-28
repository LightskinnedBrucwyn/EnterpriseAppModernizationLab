#!/usr/bin/env bash
set -euo pipefail

cd "$HOME/bat-household-hub"
git pull
cd projects/BatHouseholdHub
docker compose up -d --build
docker compose ps
curl --fail --silent --show-error http://127.0.0.1:5188/bills >/dev/null
echo "BAT_HOUSEHOLD_DEPLOY_OK"
