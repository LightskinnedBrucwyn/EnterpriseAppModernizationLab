# Batserver deployment

The application runs in Docker on port `5188`. Persistent household data and
ASP.NET encryption keys are stored in the host-side `data` directory.

The host machine's Tailscale hostname is `letsgetrichbabe` (renamed from the
original `batserver`; anyone on the tailnet can reach it at that name).

From the Windows development machine:

```powershell
scp -r .\projects\BatHouseholdHub batserver@letsgetrichbabe:~/bat-household-hub
ssh batserver@letsgetrichbabe
```

On the host:

```bash
cd ~/bat-household-hub
docker compose up -d --build
docker compose ps
```

Open `http://letsgetrichbabe:5188` from another device on the tailnet.

For later updates, rebuild the deploy tarball, copy it over, and rerun the
deploy script (see `deploy/deploy-batserver.sh`):

```bash
docker compose up -d --build
```

Back up the `~/bat-household-hub/data` directory. Do not expose port 5188 to
the public internet until authentication and an HTTPS reverse proxy are added.
