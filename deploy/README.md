# Automatic deployment (GitHub Actions → Docker container on your server)

Every push to `main` that touches the web app:

1. builds a Docker image on GitHub and pushes it to the GitHub container registry
   (`ghcr.io/<your-user>/imageconverter:<commit>`),
2. connects to your server over SSH and runs `deploy <commit>`,
3. the server pulls the image, restarts the container on your configured port,
   checks that it answers – and rolls back to the previous image if it doesn't.

```
GitHub Actions ──build──▶ ghcr.io/<user>/imageconverter:<commit>
       │                                   ▲ pull
       └──ssh "deploy <sha>"──▶ deploy user ─sudo (1 script)─▶ imageconverter-deploy ─▶ docker compose
                                                                container "imageconverter"
                               nginx / your proxy ─▶ 127.0.0.1:<IMAGECONVERTER_PORT> ─▶ :8080 in container
```

## Who gets which rights

| Account | Purpose | Rights |
|---|---|---|
| `root` | you | **never used by GitHub** |
| `deploy` | GitHub Actions | SSH key only. The key is bound to one *forced command*: it can only run `imageconverter-deploy deploy <commit-sha>`, `status` or `restart` – no shell, no file upload, no port forwarding. **Not** in the `docker` group (that group is equivalent to root). |
| container | runs the app | non-root user, read-only filesystem, no Linux capabilities, CPU/RAM limits |

The compose file, the settings file and the deploy script are owned by root, so the deploy user
cannot change what runs. If the GitHub secret leaks, the worst case is "deploy another commit of this app".

## Configuration on the server

Everything is in **`/etc/imageconverter/imageconverter.env`** (never overwritten by deployments):

| Setting | Default | Meaning |
|---|---|---|
| `IMAGECONVERTER_IMAGE` | – | `ghcr.io/<github-user>/imageconverter` (lowercase) |
| `IMAGECONVERTER_PORT` | `5080` | host port – choose one that is free next to your other apps |
| `IMAGECONVERTER_BIND` | `127.0.0.1` | `127.0.0.1` = only via your reverse proxy; `0.0.0.0` = open to the network |
| `IMAGECONVERTER_CPUS` | `2` | max CPU cores the container may use |
| `IMAGECONVERTER_MEMORY` | `4g` | max RAM |
| `TspDefaults__AutoPixelsPerPoint` etc. | – | app settings (override `appsettings.json`) |

After editing: `sudo imageconverter-deploy restart`.

## One-time setup

**Requirements on the server:** Docker with the compose plugin (`docker compose version`), curl.

### 1. Create a deploy key (on your PC)

```bash
ssh-keygen -t ed25519 -f imageconverter_deploy -N "" -C "github-actions-deploy"
```
`imageconverter_deploy` (private) → GitHub secret, `imageconverter_deploy.pub` (public) → server.
Use this key for nothing else.

### 2. Prepare the server (once, as root)

Copy the `deploy/` folder to the server, then:
```bash
sudo bash deploy/setup-server.sh --image ghcr.io/<github-user>/imageconverter --port 5080 \
     "$(cat imageconverter_deploy.pub)"
```
Re-running it is safe (keeps your settings file).

**Private repository?** Then the image is private too and the server must log in once:
```bash
sudo docker login ghcr.io -u <github-user>
# password: a classic GitHub token with ONLY the "read:packages" scope
```
(Or make the package public: GitHub → your profile → Packages → imageconverter → Package settings.)

### 3. GitHub secrets

Repository → **Settings → Environments → New environment** `production` → **Environment secrets**:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | server hostname or IP |
| `DEPLOY_USER` | `deploy` |
| `DEPLOY_SSH_KEY` | full content of `imageconverter_deploy` (private key) |
| `DEPLOY_KNOWN_HOSTS` | output of `ssh-keyscan -t ed25519 -p 22 <host>` (compare with the line printed by setup-server.sh) |
| `DEPLOY_PORT` | only if SSH is not on port 22 |

The image push uses the built-in `GITHUB_TOKEN` – nothing to configure.
Optional: enable **Required reviewers** on the environment to approve every deployment.

### 4. Reverse proxy / HTTPS / password

See `nginx-imageconverter.conf` – set `proxy_pass` to your `IMAGECONVERTER_PORT`.
The app has no login and a conversion can use all CPU cores it is given, so keep the password
protection unless the server is only reachable from your own network.

### 5. Deploy

Push to `main`, or Actions → *Deploy web app* → **Run workflow**.

## Everyday commands (on the server)

```bash
sudo imageconverter-deploy status           # image, port, container state
sudo imageconverter-deploy restart          # after editing the env file
docker logs -f imageconverter               # live logs
cat /var/lib/imageconverter/previous-tag    # previous version
sudo imageconverter-deploy deploy <sha>     # manual (re)deploy / rollback to any pushed commit
```

## Test locally

```bash
docker build -f src/ImageConverter.Web/Dockerfile -t imageconverter .
docker run --rm -p 5080:8080 imageconverter        # → http://localhost:5080
```
