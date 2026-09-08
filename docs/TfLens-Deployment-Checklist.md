# TfLens — Deployment Checklist

| | |
|---|---|
| App | TfLens |
| Hosting target | VPS |
| Pipeline document | `docs/claude-code-deployment-brief-v3.2.md` |
| Date | 2026-09-06 |
| Proven | never |

Running TfLens locally, or with its self-contained `docker-compose.yml` (own Postgres container), is **not**
in this document — that is `docs/TfLens-UsageGuide.md`'s Execution guide ("Local" and "Deployment
(`docker compose`)" runbooks). This document covers exactly one hosting target: `tflens.techierathore.com`
on the shared VPS, deployed by `.github/workflows/deploy.yml`, using the server's native PostgreSQL, Caddy
and Seq, per `docs/claude-code-deployment-brief-v3.2.md` and the server built by
`docs/bluehost-vps-runbook-v5.2.md`.

## 1. Who does what

| Step | Done by |
|---|---|
| Server build and preflight (runbook, one time) | owner |
| DNS A record, Caddy site file, Seq API key (one time per app, §3) | owner |
| Four org-level + four repo-level GitHub secrets (one time, §2) | owner |
| Build the image and push to GHCR | pipeline (`build` job) |
| Render `docker-compose.prod.template.yml`, create the database, upload and start the container | pipeline (`deploy` job) |
| Probe `/healthz` through the real URL, fail the run if it never answers | pipeline (`verify` job) |
| `git push origin main` (the deploy trigger) | owner |
| First-run verification against the live URL, UptimeRobot monitor | owner |

## 2. Secrets and settings

| Name | Where it is set | What breaks without it |
|---|---|---|
| `VPS_HOST` | GitHub Actions secret (org or repo) | SSH steps have no target; every `deploy` step fails |
| `VPS_USER` | GitHub Actions secret — value `ciuser`, never `ravi` | SSH steps fail, or (if `ravi`) exceed the CI account's intended privilege boundary |
| `VPS_SSH_KEY` | GitHub Actions secret — full private key incl. `-----BEGIN`/`-----END` | SSH steps fail with permission denied |
| `DB_PASSWORD` | GitHub Actions secret (`appuser`'s password, runbook Step 6). Must contain no `$` or `;` — the value is substituted into a connection string inside a compose file that `docker compose` itself then interpolates | The `deploy` job's own preflight fails the run before touching the server (empty value); a `$`/`;` character corrupts the rendered connection string silently |
| `SEQ_API_KEY` | GitHub Actions repo secret — a key titled `tflens` with applied property `App=tflens` (§3) | Deploy still succeeds (warning in the Actions log); TfLens logs to console and file only, nothing reaches Seq |
| `TFLENS_GITHUB_TOKEN` | GitHub Actions repo secret — fine-grained, Public Repositories (read-only), per `docs/TfLens-UsageGuide.md` §3 of the guide's token instructions | Deploy still succeeds (warning); GitHub reads fall back to 60/hour and sync fails on more than one or two connected repositories |
| `TFLENS_APPMANAGER_API_KEY` | GitHub Actions repo secret | Set together with the secret below, or both left empty. A half pair makes AppManager answer `401 INVALID_API_KEY` on every call; the `deploy` job's own preflight catches this and fails the render step before it reaches the server (`DECISIONS.md` D-006) |
| `TFLENS_APPMANAGER_API_SECRET` | GitHub Actions repo secret | As above |
| DNS A record `tflens` → VPS IP | Domain registrar (DNS only / grey cloud if the domain sits on Cloudflare) | The `verify` job resolves the real hostname and fails even though the app is running correctly |
| `/srv/caddy/sites/tflens.caddy` | Hand-written once on the server (never by the pipeline) | No route to the container; `https://tflens.techierathore.com` answers `502` or nothing at all |

## 3. Before the first deploy

- [ ] `ssh ravi@YOUR_VPS_IP 'sudo /srv/checkup.sh'` — reports `0 failed`
- [ ] `ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP 'whoami; docker ps --format "{{.Names}}"; sudo -l'` — prints `ciuser`, lists `caddy` and `seq` among the running containers, and `sudo -l` lists **only** `/usr/local/bin/ensure-db`
- [ ] `ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP 'touch /srv/apps/.t && rm /srv/apps/.t && echo OK; touch /srv/caddy/sites/.t && rm /srv/caddy/sites/.t && echo OK'` — prints `OK` twice
- [ ] Add the four org-level secrets (`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD`) at the org or repo level — GitHub → Settings → Secrets and variables → Actions lists all four (inherited org secrets count)
- [ ] Create TfLens's own Seq API key: `https://seq.techierathore.com` → Settings → API Keys → Add API Key, title `tflens`, applied property `App=tflens` → save it as the repo secret `SEQ_API_KEY` — the repo's Actions secrets page lists `SEQ_API_KEY`
- [ ] Create the fine-grained, public-read-only GitHub token per `docs/TfLens-UsageGuide.md`'s token instructions, verify it with the `rate_limit` curl (prints `limit=5000`), then save it as the repo secret `TFLENS_GITHUB_TOKEN`
- [ ] Add `TFLENS_APPMANAGER_API_KEY` and `TFLENS_APPMANAGER_API_SECRET` **together, or leave both unset** — the repo's Actions secrets page shows both present or both absent, never one alone
- [ ] Add the DNS `A` record (Name `tflens`, Value = VPS IP, DNS-only/grey-cloud on Cloudflare) at the registrar — `dig +short tflens.techierathore.com` returns the VPS IP
- [ ] Write `/srv/caddy/sites/tflens.caddy` on the server (`tflens.techierathore.com { reverse_proxy tflens:8080 }`) and run `docker exec caddy caddy reload --config /etc/caddy/Caddyfile` — `docker exec caddy caddy validate --config /etc/caddy/Caddyfile` passes and `curl -I https://tflens.techierathore.com` returns a certificate (a `502` body at this stage is expected — nothing is running behind it yet)
- [ ] `grep -c '\${' docker-compose.prod.template.yml` and a manual read of the file — every `${...}` is one of the five envsubst names (`DB_PASSWORD`, `SEQ_API_KEY`, `TFLENS_APPMANAGER_API_KEY`, `TFLENS_APPMANAGER_API_SECRET`, `TFLENS_GITHUB_TOKEN`); no real value is committed

## 4. Deploy

- [ ] `git push origin main` — a `deploy` workflow run appears under **Actions**
- [ ] Watch the `build` job — green, and `ghcr.io/techierathore/tflens` gains a `:latest` tag and a short-SHA tag
- [ ] Watch the `deploy` job — green; its steps create `/srv/apps/tflens` and `/srv/data/tflens/{data,logs}`, run `sudo ensure-db tflens` on the server, upload the rendered `docker-compose.yml`, then `docker compose pull && up -d`
- [ ] Watch the `verify` job — green; it curls `https://tflens.techierathore.com/healthz` (20s initial wait, up to 5 attempts, 15s apart) and fails the run loudly on anything but 200

## 5. After the deploy

- [ ] `curl -s https://tflens.techierathore.com/healthz` — JSON with `"status":"ok"` and `"database":"up"`
- [ ] Open `https://seq.techierathore.com`, filter `App = 'tflens'` — TfLens's startup lines appear
- [ ] Add one UptimeRobot HTTP(s) monitor on `https://tflens.techierathore.com/healthz` — the monitor's first check reports up
- [ ] `ssh ravi@YOUR_VPS_IP 'sudo /srv/checkup.sh'` — still `0 failed`, `tflens` running and not restart-looping, `caddy config valid`
- [ ] Walk `docs/TfLens-UsageGuide.md`'s first-run verification against the real URL: sign in, connect a public repository, **Sync now**, Coverage shows the repo with a non-empty stream count, `/gate-outcomes` · `/harness` · `/routing` render, `/export` writes a snapshot — this is the acceptance test; a green pipeline only proves `/healthz` answers

## 6. Rollback

- [ ] **Clean path**: `git revert <bad commit> && git push` — a new `deploy` run redeploys the previous code exactly like any other release
- [ ] **Fast path** (only when Actions itself is down): on the server, edit `/srv/apps/tflens/docker-compose.yml` to pin `image: ghcr.io/techierathore/tflens:<short-sha>` (every deployed SHA is already in GHCR), then `docker compose pull && docker compose up -d` — `docker compose ps` shows the pinned SHA running and `/healthz` answers 200
- [ ] Change the tag back to `:latest` once the fix is pushed, or the next pipeline run re-pins the old image

A rollback does not undo: the raw archive at `/srv/data/tflens/data` (no migrations exist — `database/001-schema.sql` is idempotent), the hand-written Caddy site file, or a secret already rotated in GitHub (only reaches the container on the *next* deploy).

## 7. Routine operations

| Task | Command |
|---|---|
| Follow the logs | `docker logs -f tflens` (on the server) |
| Health from inside the container | `docker exec tflens wget -qO- http://localhost:8080/healthz` |
| One sync pass now | `docker exec tflens dotnet TfLens.dll sync` |
| Rebuild a user's streams from the raw archive | `docker exec tflens dotnet TfLens.dll rebuild --user <id>` |
| Export a snapshot | `docker exec tflens dotnet TfLens.dll export --user <id> [--framework techieflow\|playbook]` |
| Restart without redeploying | `cd /srv/apps/tflens && docker compose restart` |
| Re-apply the current image (e.g. after rotating a secret) | GitHub → Actions → `deploy` → **Run workflow** (`workflow_dispatch`) |
| Change routing | edit `/srv/caddy/sites/tflens.caddy`, then `docker exec caddy caddy validate --config /etc/caddy/Caddyfile` before `docker exec caddy caddy reload --config /etc/caddy/Caddyfile` |

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| SSH step fails: permission denied | `VPS_SSH_KEY` holds the `.pub` file, or `VPS_USER` is `ravi` instead of `ciuser` | Re-paste the full private key incl. `BEGIN`/`END`; set `VPS_USER` to `ciuser` |
| `sudo: a password is required` | A step used sudo for something outside the allowlist | Only `/usr/local/bin/ensure-db` is permitted — remove the sudo |
| `docker compose pull` → `unauthorized` | The server's stored GHCR PAT expired (runbook Step 12) — not a pipeline or secret problem | Re-run the GHCR login from runbook Step 12 on the server |
| `verify` job fails, DNS error | The A record is missing or has not propagated | Add it (§3), then re-run the job |
| `verify` job fails, 502 in the browser | Container crash-looping | `docker logs tflens` — read the actual error before changing anything |
| Container logs: refuses to start, missing DB connection | `DB_PASSWORD` empty, or a `$`/`;` in the password mangled the connection string | Reset the secret without those characters and re-run `deploy` |
| Container logs: `INVALID_API_KEY` at startup | A half AppManager pair reached the server | Set both `TFLENS_APPMANAGER_API_KEY`/`_SECRET` or neither (`DECISIONS.md` D-006) — the `deploy` job's preflight is meant to catch this before it ever reaches the server |
| `/healthz` answers 503 | Database unreachable | Connection string must use `172.17.0.1`, never `localhost` or a container name; confirm `ensure-db tflens` ran in the `deploy` job log |
| Every repository shows `error` with a rate-limit message | `TFLENS_GITHUB_TOKEN` empty or expired | Recreate the token (§3), update the secret, re-run `deploy` via `workflow_dispatch` |
| Runs fine, no logs in Seq | `SEQ_API_KEY` missing or revoked | Recreate the key in Seq (§3), update the secret, re-run `deploy` |
| Coverage empty after a restore | Rows were not replayed after `/srv/data/tflens/data` was restored | `docker exec tflens dotnet TfLens.dll rebuild --user <id>` |
| New app's Caddy site file was never created | TfLens's pipeline deliberately does not automate Caddy — see the contradiction below | Write `/srv/caddy/sites/tflens.caddy` by hand and reload (§3) |
| Pipeline document's step order looks contradicted by this checklist | `claude-code-deployment-brief-v3.2.md` §1 sequences `sudo ensure-db` **before** placing the compose file, while its §4 master prompt sequences the compose upload and the Caddy step **before** `ensure-db`; the brief also allows the pipeline to create the Caddy snippet automatically | This checklist and `.github/workflows/deploy.yml` are the actual, authoritative order for TfLens: mkdir → `ensure-db` → upload compose → start; TfLens's workflow contains no Caddy step at all, by deliberate owner choice — the code, not either brief section, is what ships |

## 9. Proven

| What | Executed for real | When |
|---|---|---|
| Deploy to `tflens.techierathore.com` | no | — |
| Rollback (clean or fast path) | no | — |
