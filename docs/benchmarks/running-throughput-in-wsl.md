# Running throughput benchmarks under WSL (with Podman)

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

**In Rider:** File → Open → paste this into the path field (replace `<your-username>` with your Ubuntu username, which `whoami` in Ubuntu will tell you):

```
\\wsl$\Ubuntu\home\<your-username>\projects\Edict\Edict\Edict.sln
```

Rider auto-detects the WSL toolchain for solutions opened from a WSL path. Build, run, debug, and test all dispatch into Ubuntu from here on.

---

## 9. Run the throughput benchmarks

**In Rider:** right-click `Edict.Benchmarks.Throughput.Tests` → Run Tests.

Or, **in Ubuntu:**

```bash
cd ~/projects/Edict/Edict
dotnet test Edict.Benchmarks.Throughput.Tests
```

Save the CSV output under `docs/benchmarks/raw/` with a `-wsl` suffix so the comparison against your Windows numbers stays auditable.
