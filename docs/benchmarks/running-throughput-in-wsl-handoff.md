# Handoff: WSL throughput-bench setup, blocked at step 7

Pairs with `docs/benchmarks/running-throughput-in-wsl.md`. If the user references that setup guide, open this doc and pick up from "Next action."

## Where we got to

Steps 1–6 of the setup guide completed clean. Step 7 (the Azurite-backed smoke test) fails — but **not** for a Testcontainers reason. A pure unit test from `Edict.Core.Tests` (filter `RowTypeResolver`, no fixtures, no containers) hits the same error:

```
vstest.console process failed to connect to testhost process after 90 seconds.
```

So the failure mode is `dotnet test` itself failing to bridge `vstest.console` ↔ `testhost` on loopback in this WSL distro. Bumping `VSTEST_CONNECTION_TIMEOUT` is not the fix — testhost is dying or never connecting, not running slow.

## Environment

- Ubuntu in WSL2, running as **`root`** (prompt: `root@Malcolm-Work:~/projects/Edict/Edict#`). Setup guide was written assuming a normal user — root works for everything we've done so far, but it's a contextual gotcha.
- `.NET 10` SDK installed via `dotnet-install.sh` (confirmed earlier with `dotnet --version` → 10.x).
- `Microsoft.NET.Test.Sdk` **18.6.0**, `xunit` **2.9.3**, `xunit.runner.visualstudio` **3.1.5** — classic VSTest path, no MTP sidestep available.
- Podman healthy: socket at `/run/user/0/podman/podman.sock`, `podman info` works, `DOCKER_HOST` matches.
- Env vars confirmed: `RYUK_DISABLED=true`, `XDG_RUNTIME_DIR=/run/user/0`.
- `/etc/hosts` correct: `127.0.0.1 localhost` and `127.0.1.1 Malcolm-Work.localdomain Malcolm-Work` both present.

## What we ruled out

| Hypothesis | Status |
| --- | --- |
| Podman socket unreachable | Ruled out — `podman info` succeeds, socket file exists with correct permissions |
| `DOCKER_HOST` mismatch | Ruled out — matches the live socket path |
| Testcontainers/Azurite fixture crash | Ruled out — fails identically on a fixture-less unit test |
| Missing `localhost` in `/etc/hosts` | Ruled out — entry present |
| IPv6 binding issue | Ruled out — `DOTNET_SYSTEM_NET_DISABLEIPV6=1` didn't change the outcome |

## Next action — paste this first, then come back to me

The diagnostic logs will tell us where testhost is actually dying. The user has not yet run these.

**In Ubuntu — confirm SDK details:**

```bash
dotnet --info | head -30
```

**In Ubuntu — capture vstest diagnostics:**

```bash
cd ~/projects/Edict/Edict
rm -f /tmp/vstest.log*
dotnet test Edict.Core.Tests --filter "FullyQualifiedName~RowTypeResolver" --no-build --diag:/tmp/vstest.log -- RunConfiguration.TreatNoTestsAsError=false
```

**In Ubuntu — show the tail of every log file produced:**

```bash
ls -la /tmp/vstest.log*
for f in /tmp/vstest.log*; do
  echo "=== $f ==="
  tail -80 "$f"
done
```

Paste back the SDK info plus the tail of each log. From there the most likely outcomes:

- **Testhost crash on startup** (missing native dep, OOM, etc.) → log will show the assembly/native lib name; install or symlink it.
- **Loopback bind failure** → log shows port/socket error; fix is usually a hosts-file edit or a `DOTNET_DiagnosticPorts` clear.
- **TLS or cert error during connection negotiation** → log shows the handshake; `dotnet dev-certs https --trust` doesn't apply on Linux but there are equivalents.
- **Test.Sdk 18.6.0 specific bug on .NET 10 + Linux** → fallback is pinning the test project to Test.Sdk 17.x and re-running, but only do that after the log rules out simpler causes.

## Suggested skill

Use **diagnose** — disciplined diagnosis loop. This is a hard environment bug with multiple plausible causes; the diag log is the next instrumentation step in that loop.

## Open questions to leave for the user (optional, don't block on these)

- Is running as `root` in the Ubuntu distro deliberate, or did the distro skip user-creation on first run? Either way it's not the blocker, but the setup guide currently assumes UID 1000 and the user-systemd socket path. If they'd rather operate as a normal user, post-fix we may want to amend the guide.
- They have an existing Podman setup on the Windows host (separate from the Ubuntu Podman we installed). Worth knowing whether they ever plan to consolidate, but again — not blocking.
