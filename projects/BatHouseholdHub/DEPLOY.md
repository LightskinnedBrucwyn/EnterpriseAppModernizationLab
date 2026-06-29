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

Open `https://letsgetrichbabe.tail447013.ts.net` from another device on the
tailnet (any device logged into the same tailnet can reach it — see the HTTPS
section below for how this is set up). The plain `http://letsgetrichbabe:5188`
URL still works too, but push notifications require the HTTPS one.

For later updates, run the deploy script:

```bash
~/deploy-batserver.sh
```

which pulls the latest commit, rebuilds, and curls `/bills` as a healthcheck.
A copy of this script lives at `deploy/deploy-batserver.sh` in the repo — keep
`~/deploy-batserver.sh` on the host in sync with it.

Back up the `data` directory. Do not expose port 5188 to the public internet
until authentication and an HTTPS reverse proxy are added.

## HTTPS via Tailscale (required for push notifications)

Service Workers — and therefore Web Push — only work in a secure context
(HTTPS or `localhost`). Plain `http://letsgetrichbabe:5188` is an insecure
origin, so `navigator.serviceWorker` doesn't exist there and the Alerts tab
will always say push is unsupported, no matter what's done on the
home-screen-icon side.

Tailscale can terminate real HTTPS for the tailnet hostname without exposing
anything to the public internet. This is already set up — `tailscale serve`
runs as a reverse proxy in front of the existing container (no duplicate app,
same `http://127.0.0.1:5188` backend), and any device on the tailnet (not
just the host) can reach the HTTPS URL the same way it reached the old one.

It was set up like this, for reference if it ever needs redoing (e.g. after
a host OS reinstall):

```bash
sudo tailscale set --operator=$USER   # one-time, avoids needing sudo below
tailscale serve --bg http://127.0.0.1:5188
tailscale serve status                # confirms the proxy config
```

The tailnet's domain is `tail447013.ts.net`, so the app is reachable at
`https://letsgetrichbabe.tail447013.ts.net` (no port — serve listens on
443). The serve config persists across reboots once set.

Update home-screen icons (and any bookmarks) on every device to the new
`https://...` URL — an icon pointing at the old `http://` URL will never
get push working since the page itself is loaded from an insecure origin.

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
