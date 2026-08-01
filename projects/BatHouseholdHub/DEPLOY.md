# BatServer private deployment

BatHouseholdHub is intended for private deployment on a trusted household host.
The GitHub repository is public; runtime data belongs only on the private host.

The application runs in Docker on a private LAN/Tailscale-only port such as
`<private-app-port>`. Persistent household data and ASP.NET data-protection keys
are stored in a host-side runtime data directory such as:

```text
<batserver-app-root>/data
```

Do not commit the runtime data directory, uploads, imports, backups, statements,
screenshots, or any copied production files.

From the Windows development machine:

```powershell
scp -r .\projects\BatHouseholdHub <batserver-user>@<tailnet-hostname>:<batserver-app-root>
ssh <batserver-user>@<tailnet-hostname>
```

On the host:

```bash
cd <batserver-app-root>
docker compose up -d --build
docker compose ps
```

Open `http://<tailnet-hostname>:<private-app-port>` from another trusted device
on the LAN or tailnet.

For later updates, rebuild the deploy tarball, copy it over, and rerun the
deploy script (see `deploy/deploy-batserver.sh`):

```bash
docker compose up -d --build
```

Back up `<batserver-app-root>/data` using encrypted private storage. Do not
expose the app to the public internet until authentication, HTTPS termination,
secret management, and audit logging are implemented and reviewed.
