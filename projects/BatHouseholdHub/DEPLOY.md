# Batserver deployment

The application runs in Docker on port `5188`. Persistent household data and
ASP.NET encryption keys are stored in the host-side `data` directory.

From the Windows development machine:

```powershell
scp -r .\projects\BatHouseholdHub batserver@batserver:~/bat-household-hub
ssh batserver@batserver
```

On Batserver:

```bash
cd ~/bat-household-hub
docker compose up -d --build
docker compose ps
```

Open `http://batserver:5188` from another device on the home network.

For later updates, copy the project again and rerun:

```bash
docker compose up -d --build
```

Back up the `~/bat-household-hub/data` directory. Do not expose port 5188 to
the public internet until authentication and an HTTPS reverse proxy are added.
