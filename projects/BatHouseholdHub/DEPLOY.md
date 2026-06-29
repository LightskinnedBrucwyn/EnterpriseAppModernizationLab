# Batserver deployment

The application runs in Docker on port `5188`. Persistent household data and
ASP.NET encryption keys are stored in the host-side `data` directory
(`~/bat-household-hub/projects/BatHouseholdHub/data` — outside the git tree).

The host machine's Tailscale hostname is `letsgetrichbabe` (renamed from the
original `batserver`; anyone on the tailnet can reach it at that name).

`~/bat-household-hub` on the host is a git clone of this repository
(`gh` is already authenticated there, so HTTPS clones work without a token).
First-time setup:

```bash
git clone https://github.com/LightskinnedBrucwyn/EnterpriseAppModernizationLab.git ~/bat-household-hub
cd ~/bat-household-hub/projects/BatHouseholdHub
docker compose up -d --build
docker compose ps
```

Open `http://letsgetrichbabe:5188` from another device on the tailnet.

For later updates, run the deploy script:

```bash
~/deploy-batserver.sh
```

which pulls the latest commit, rebuilds, and curls `/bills` as a healthcheck.
A copy of this script lives at `deploy/deploy-batserver.sh` in the repo — keep
`~/deploy-batserver.sh` on the host in sync with it.

Back up the `data` directory. Do not expose port 5188 to the public internet
until authentication and an HTTPS reverse proxy are added.

## Push notifications (bill-due alerts)

Push notifications need a one-time VAPID keypair set as host environment
variables before `docker compose up` picks them up:

```bash
export VAPID_PUBLIC_KEY="..."
export VAPID_PRIVATE_KEY="..."
```

Add those two lines to `~/.bashrc` (or wherever `deploy-batserver.sh` runs
from) so they persist across reboots. Without them, the app still runs fine —
the "Enable notifications" option in Quick Tools just stays unavailable.

## Bank auto-sync (Plaid)

The Banking page (`/banking`) connects a real bank account so transactions
sync in automatically instead of manual Rocket Money CSV exports. Needs a
free Plaid developer account:

1. Sign up at https://dashboard.plaid.com/signup — the free tier includes
   unlimited Sandbox use and a limited number of live ("Production") items,
   enough for one household.
2. From the Plaid dashboard, grab the `client_id` and the **Sandbox** secret
   first (test with the fake bank logins Plaid provides — no real bank
   credentials needed yet).
3. Set host environment variables, same pattern as the push keys above:

```bash
export PLAID_CLIENT_ID="..."
export PLAID_SECRET="..."       # the Sandbox secret, to start
export PLAID_ENV="sandbox"      # switch to "production" + the Production secret when ready
```

4. Restart the container (`docker compose up -d --build` or
   `~/deploy-batserver.sh`) so the new env vars are picked up.

Without these set, `/banking` just shows "Plaid isn't configured yet" — the
rest of the app is unaffected. A background job syncs connected accounts
every 6 hours; synced transactions get the same auto-matching against bills
that the CSV import uses, so a bank-confirmed bill payment still marks the
bill Paid automatically.
