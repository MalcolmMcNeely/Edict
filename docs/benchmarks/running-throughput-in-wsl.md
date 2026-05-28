# Running throughput benchmarks under WSL (with Podman)

> **Setup in progress.** A first run paused at step 7 with `dotnet test` failing to start testhost in WSL — diagnostic plan and ruled-out hypotheses are in [`running-throughput-in-wsl-handoff.md`](./running-throughput-in-wsl-handoff.md). Pick up there before re-running step 7.

Run `Edict.Benchmarks.Throughput.Tests` inside a WSL2 Ubuntu distro with rootless Podman in the same VM, so the .NET process and the Azurite/Postgres/Kafka containers share one network. No Windows-to-container network hop.

**Assumes:** Windows 11, WSL2 + Ubuntu already installed. Your existing Windows-side Podman is left alone — a separate Podman is installed inside Ubuntu.

**Convention:** every command block has a heading saying where to paste it. "Ubuntu" means the Ubuntu terminal (Start menu → Ubuntu). "PowerShell" means a Windows PowerShell window.

---

## 1. Enable systemd in Ubuntu

Rootless Podman needs user-systemd. WSL doesn't enable it by default.

**In Ubuntu:**

```bash
sudo tee /etc/wsl.conf > /dev/null <<'EOF'
[boot]
systemd=true
EOF
```

**In PowerShell:**

```powershell
wsl --shutdown
```

Re-open Ubuntu from the Start menu, then:

**In Ubuntu:**

```bash
systemctl is-system-running
```

Expect `running` or `degraded`. If it says `offline`, systemd didn't enable — repeat this step.

---

## 2. Install Podman in Ubuntu

**In Ubuntu:**

```bash
sudo apt update && sudo apt install -y podman
podman run --rm hello-world
```

Expect a "Hello from Docker!" banner.

If you get a subuid error:

**In Ubuntu:**

```bash
sudo usermod --add-subuids 100000-165535 --add-subgids 100000-165535 $USER
podman system migrate
podman run --rm hello-world
```

---

## 3. Enable the Podman socket

Testcontainers .NET talks to this socket.

**In Ubuntu:**

```bash
systemctl --user enable --now podman.socket
systemctl --user status podman.socket
```

Expect `Active: active (listening)` and a `Listen:` line ending in `/podman/podman.sock`. Press `q` to exit the status view.

---

## 4. Set Testcontainers environment variables

**In Ubuntu:**

```bash
cat >> ~/.bashrc <<'EOF'

# Testcontainers + Podman
export XDG_RUNTIME_DIR=/run/user/$(id -u)
export DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock
export TESTCONTAINERS_RYUK_DISABLED=true
EOF

source ~/.bashrc
echo $DOCKER_HOST
```

Expect `unix:///run/user/1000/podman/podman.sock` (the `1000` is your user ID; may differ).

Ryuk is Testcontainers' auto-cleanup sidecar. It misbehaves with rootless Podman, so it's disabled. If a test run crashes, clean up with `podman ps -a` then `podman rm <id>`.

---

## 5. Install .NET 10 SDK + git in Ubuntu

**In Ubuntu:**

```bash
sudo apt install -y git wget
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir $HOME/.dotnet

cat >> ~/.bashrc <<'EOF'

# .NET 10
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH
EOF

source ~/.bashrc
dotnet --version
```

Expect `10.x`.

---

## 6. Clone Edict inside Ubuntu

Keep this clone separate from your Windows checkout — Linux has different line endings, case sensitivity, and bin/obj artifacts.

**In Ubuntu:**

```bash
mkdir -p ~/projects
cd ~/projects
git clone https://github.com/MalcolmMcNeely/Edict.git
cd Edict
```

Git will prompt for credentials on first push. Use a GitHub Personal Access Token as the password.

---

## 7. Smoke test

A small Azurite-backed test proves Testcontainers + Podman + WSL + .NET are wired.

**In Ubuntu:**

```bash
cd ~/projects/Edict/Edict
dotnet test Edict.Azure.Tests --filter "FullyQualifiedName~AzureControllableUpsertRow" --logger "console;verbosity=normal"
```

Expect green tests. If you get a socket error, re-check step 3 (socket active) and step 4 (env vars printed).

---

## 8. Open the WSL clone in Rider

Rider runs on **Windows**, not inside Ubuntu (Ubuntu in WSL2 is CLI-only). Windows exposes the Ubuntu filesystem as a network share at `\\wsl$\Ubuntu\...`, so Rider on Windows opens the solution from there and its WSL toolchain dispatches builds and test runs into the Ubuntu distro.

**In Rider on Windows:** File → Open → paste the path into the path field.

- If you set up a normal user in Ubuntu (typical case):

  ```
  \\wsl$\Ubuntu\home\<your-username>\projects\Edict\Edict\Edict.sln
  ```

  Replace `<your-username>` with your Ubuntu username (`whoami` in Ubuntu prints it).

- If you're running as root in Ubuntu (root's home is `/root`, not `/home/root`):

  ```
  \\wsl$\Ubuntu\root\projects\Edict\Edict\Edict.sln
  ```

Rider will *suggest* the WSL toolchain when it loads the solution, but doesn't always apply it automatically. Confirm/configure it in the next step before running anything.

---

## 9. Switch Rider's toolchain to WSL

**Why this step matters.** Without it, Rider opens the solution from the `\\wsl$\` share but still builds and runs with the **Windows** .NET SDK. The .NET process would run on Windows and reach Podman containers across the Hyper-V vSwitch — the exact hop this whole exercise is meant to eliminate. So this step is the difference between a real WSL colocation run and a slower-than-before Windows run.

**In Rider on Windows — first-run popup (easiest path):**

When Rider finishes loading the solution from a `\\wsl$\` path, it often pops a notification saying something like "WSL detected. Use WSL toolchain?" Click **Yes / Apply**, let it reload, and skip to the verify step below.

**In Rider on Windows — manual path (if the popup didn't appear, or you dismissed it):**

1. Open Settings: **File → Settings** (or Ctrl+Alt+S).
2. Navigate to **Build, Execution, Deployment → Toolset and Build**.
3. In the **Use MSBuild version** dropdown, choose the entry that says **WSL: Ubuntu** (Rider auto-registers it when the distro is installed). If no such entry exists, click the **+** next to the dropdown → **Add WSL** → pick **Ubuntu**.
4. The **.NET CLI executable path** field should auto-populate. Verify it points to the `dotnet` you installed in step 5 — typically `/root/.dotnet/dotnet` (root) or `/home/<user>/.dotnet/dotnet` (normal user). If it shows `/usr/bin/dotnet` or is blank, override it to the `.dotnet/dotnet` path.
5. **Apply → OK**. Rider reloads the solution and re-runs restore inside WSL — first restore is slow because NuGet caches inside Ubuntu separately from Windows.

**Verify it took:**

- In Rider, open the **Terminal** tab (Alt+F12). The prompt should be a WSL Ubuntu shell, not PowerShell. Run `dotnet --version` and confirm it matches what you got in step 5.
- Right-click `Edict.Core` in the solution explorer → Build. Watch the build output — it should show paths like `/root/projects/Edict/...`, not `C:\...`.

If either check still shows Windows paths, the toolchain switch didn't apply — go back into Settings → Toolset and Build and re-select the WSL entry as the active toolset, then Apply.

---

## 10. Run the throughput benchmarks

**In Rider:** right-click `Edict.Benchmarks.Throughput.Tests` → Run Tests.

Or, **in Ubuntu:**

```bash
cd ~/projects/Edict/Edict
dotnet test Edict.Benchmarks.Throughput.Tests
```

Save the CSV output under `docs/benchmarks/raw/` with a `-wsl` suffix so the comparison against your Windows numbers stays auditable.
