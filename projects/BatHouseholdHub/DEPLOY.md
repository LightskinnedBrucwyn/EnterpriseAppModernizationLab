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
