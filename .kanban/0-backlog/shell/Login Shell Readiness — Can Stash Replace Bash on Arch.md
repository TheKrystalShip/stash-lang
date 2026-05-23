# Login Shell Readiness — Can Stash Replace Bash on Arch?

> **Status:** Analysis (backlog) — research only, no implementation
> **Created:** 2026-05-01
> **Author:** Spec Architect (with user)
> **Companions:**
>
> - [Bare Command Execution — Shell Mode for the REPL](Bare%20Command%20Execution%20%E2%80%94%20Shell%20Mode%20for%20the%20REPL.md)
> - [Can Stash Become a Shell? — Architectural Analysis](Can%20Stash%20Become%20a%20Shell%20%E2%80%94%20Architectural%20Analysis.md)
> - [docs/Shell — Interactive Shell Mode.md](../../docs/Shell%20%E2%80%94%20Interactive%20Shell%20Mode.md)

---

## TL;DR

**No, `chsh -s /usr/local/bin/stash $USER` will not brick your Arch box** — `/bin/sh` (bash on Arch) still runs system scripts, pacman doesn't touch `$SHELL`, and you can recover from any TTY as root. **But your daily-driver experience will break in several concrete and non-obvious places**, the worst of which is graphical login under most display managers. The five blockers below need to land before stash is _daily-able_; the seven items after that are quality-of-life issues you'll hit weekly.

The current shell mode is a **good interactive REPL** and a respectable Bash-substitute for ad-hoc command typing. It is **not yet a POSIX-compatible login shell**, and a few of the gaps (job control, `-c` shell-mode, login-shell argv detection) are non-trivial.

---

## 1. What Already Works (as of v1 shell mode)

The following are shipped and working on Linux/macOS — see `docs/Shell — Interactive Shell Mode.md` and the four "shipped" companion specs:

- **Bare command execution** in the REPL (`ls -la`, `git status`).
- **Streaming pipelines** (`tail -f x | grep y` works — buffered pipes were replaced; `RunPassthroughPipeline` / `RunRedirectedPipeline` use OS pipes via `ExecPipelineStreaming`).
- **Redirects** (`>`, `>>`, `2>`, `&>`).
- **Argument expansion**: `${expr}` interpolation, `{a,b}` brace expansion, `~` tilde, word splitting, glob (`*`, `?`, `[…]`, `**`).
- **Built-in sugar**: `cd`, `pwd`, `exit`, `quit` (all single-stage; multi-stage routes through VM pipelines).
- **Directory stack** (`pushd`/`popd` via `env.*`), `$?` REPL sugar.
- **RC file**: `$XDG_CONFIG_HOME/stash/init.stash` → `~/.config/stash/init.stash` → `~/.stashrc`.
- **Persistent history** with `history` built-in.
- **Customizable prompt** (themes, starters, OSC 133 markers).
- **Tab completion** (paths, commands, Stash symbols).
- **Ctrl-C cleanup**: `Console.CancelKeyPress` reaps tracked child processes.
- **Strict pipelines** (`!cmd`, `$!()`) raise `CommandError` on non-zero exit.
- **`-c "command"` flag**: parses command-string mode (but see §3.1 — it does **not** activate shell mode).
- **Piped stdin** (`echo "..." | stash`) — same caveat (Stash-only).
- **Soft-keyword `unset`** to drop accidental shadows of PATH binaries.

That's a lot. It's enough to use `stash --shell` in a terminal emulator as your daily REPL **as long as you also have a working bash login**.

---

## 2. What `chsh -s` Actually Triggers on Arch

Understanding the failure modes requires walking through what `$SHELL` actually does on a modern Arch system:

| Trigger                                   | Caller                             | What it executes                                             |
| ----------------------------------------- | ---------------------------------- | ------------------------------------------------------------ |
| TTY login / `login`                       | `agetty` → `login`                 | `exec -l $SHELL` (argv[0] = `-stash`, login-shell semantics) |
| `su -` / `sudo -i`                        | sudo, util-linux                   | `exec -l $SHELL`                                             |
| SSH interactive: `ssh user@host`          | `sshd`                             | `exec -l $SHELL`                                             |
| SSH command: `ssh user@host 'ls -la'`     | `sshd`                             | `$SHELL -c 'ls -la'`                                         |
| `scp` / `rsync` / `sftp`                  | sshd                               | `$SHELL -c 'scp -t /path'` (or sftp-server)                  |
| Display manager session start             | gdm / sddm / lightdm / ly          | `$SHELL -l -c 'startplasma-wayland'` (or similar)            |
| `xdg-open <something with spaces>`        | xdg-utils                          | `$SHELL -c '...'` in some code paths                         |
| Terminal emulator new tab                 | gnome-terminal / kitty / alacritty | `exec -l $SHELL` (interactive, usually login-flagged)        |
| tmux/screen pane                          | tmux                               | `exec -l $SHELL`                                             |
| `system(3)` / `popen(3)` from any program | libc                               | `/bin/sh -c '...'` (NOT `$SHELL` — safe)                     |
| pacman hooks, makepkg, systemd units      | system                             | `/bin/sh` or explicit shebang (NOT `$SHELL` — safe)          |
| cron                                      | cronie                             | `/bin/sh` (NOT `$SHELL` — safe)                              |

**The "safe" rows are why your install survives.** Anything that doesn't read `$SHELL` keeps working. Pacman, systemd, cron, kernel-init, and every shebang script all bypass your login shell.

**The "unsafe" rows are where stash needs to deliver.** Currently, several of them silently fail or produce wrong behavior.

---

## 3. Blockers — These Will Visibly Break

### 3.1 `-c "command"` runs in **Stash-only mode**, not shell mode

**Where it bites:** SSH remote commands, `scp`, `rsync`, every display manager, `sudo $SHELL -c`, and every script that wants to run a single command via your shell.

**What happens today:** `Stash.Cli/Program.cs:246` calls `RunSource(commandString, …)` for `-c`. `ShellRunner` is only constructed in the interactive REPL path (`Program.cs:918`). So:

```bash
$ stash -c 'ls -la'
# Parse error: 'ls' is an undefined identifier, '- la' is invalid
```

**What it needs:** `-c` (and stdin-pipe input when terminal is a TTY) must run each line through `ShellLineClassifier` + `ShellRunner` exactly the way the REPL does. The mode should default to _shell_ for `-c` because that's what every external caller assumes (POSIX `sh -c` semantics). A separate `--lang stash -c` or `-cs` flag could opt into pure-Stash interpretation for callers that explicitly want it.

**Severity: blocker.** Without this, your display manager won't start a session, `ssh host cmd` is broken, and `git commit` (which spawns `$SHELL -c $EDITOR`) fails when `$EDITOR` contains spaces or arguments.

### 3.2 Login-shell invocation is not detected

**Where it bites:** TTY login, `su -`, `ssh user@host`, every "start a login shell" entry point.

**What happens today:** POSIX login shells are signalled by argv[0] starting with `-` (e.g. `-stash`). `Program.cs` never inspects argv[0]. There is also no `-l` / `--login` flag. As a result:

- The session enters interactive REPL mode if a TTY is attached, **but only loads the RC file** — not a login profile (e.g. `/etc/profile`-equivalent or `~/.profile`-equivalent). Whatever PATH/locale/XDG-vars the parent set up are inherited, but if you're coming from `agetty` they're minimal.
- `--shell` mode is auto-enabled only if `~/.stashrc` exists (per `RcFileLoader.FindRcFile`). New-user installs without an RC file get the Stash-only REPL on TTY login.

**What it needs:**

1. Detect `argv[0]` starting with `-` → set login-shell flag.
2. Add a `-l` / `--login` flag for explicit selection.
3. Define a system-wide profile path: `/etc/stash/profile.stash` (or equivalent) loaded only on login-shell startup, before the user RC.
4. Define a user profile path distinct from the RC: e.g. `~/.config/stash/login.stash` or honour `~/.profile` (POSIX-standard but not Stash syntax — risky).
5. Document the load order: system profile → user profile → user RC (interactive only).

**Severity: blocker** (silent — your env will be broken in subtle ways: missing `PATH` extensions, no XDG vars, locale wrong).

### 3.3 No `exec` shell built-in (process replacement)

**Where it bites:** `exec startx`, `exec ssh server`, login-script idioms (`exec /usr/bin/zsh` is how people swap mid-session), and **every display-manager session script** which typically does `exec dbus-launch --exit-with-session $SESSIONCMD`.

**What exists:** `process.exec(command)` in stdlib does call POSIX `execvp` via direct P/Invoke equivalent on Unix (actually no — it currently uses `Process.Start` + `Environment.Exit(child.ExitCode)` per the Windows branch, but the Unix branch does true `execvp`). However:

1. It is **deprecated alias** for some other namespace? No — checked, `process.exec` is the canonical. But it's a stdlib function call (`process.exec("startx")`), not a shell-mode word. Typing `exec startx` at the prompt today either runs `/usr/bin/exec` (doesn't exist) or fails classification.
2. Display-manager session scripts will do `exec systemd-cat -t plasma startplasma-wayland` — multi-word, with arguments. The shell built-in must accept the rest of the line as the command.

**What it needs:** A shell-sugar `exec` (alongside `cd`, `pwd`, `exit`) that desugars to `process.exec("…")` with proper argv handling. Must run last in the shell flow — after defers/cleanup — because it never returns.

**Severity: blocker** for graphical login. Workaround: write a one-line bash wrapper.

### 3.4 No job control whatsoever

**Where it bites:**

- `vim`, `nano`, `htop`, `less`, `man`, `ssh -t` — anything that uses the terminal in raw mode and expects to receive Ctrl+Z (SIGTSTP) and be backgrounded.
- `make &`, `npm run dev &`, any "fire it and keep working" workflow.
- `Ctrl+C` propagates to **both** stash and the foreground child because they share a process group. For most simple commands this is fine (child handles SIGINT, exits, stash sees the exit code). But `Console.CancelKeyPress` in stash also fires and runs `CleanupTrackedProcesses()` — which can race with the child's own SIGINT handler.

**What it needs:**

- P/Invoke for `setpgid()`, `tcsetpgrp()`, `tcgetpgrp()`.
- Each foreground command runs in a fresh process group; stash transfers terminal ownership before exec, takes it back on child exit.
- SIGTSTP handler that yields the terminal back to stash and registers the stopped job.
- A jobs table, `&` suffix parser, `jobs`/`fg`/`bg`/`%n` built-ins.
- `wait` built-in.
- SIGCHLD handler to detect background completion.

This is **the largest single piece of work** and is what stalls most "let's add a shell" projects. The companion analysis flagged it correctly.

**Severity: blocker** for _daily_ use, not for _trial_ use. You can survive without job control by using tmux for backgrounding, but `vim` gets ugly when Ctrl+Z silently does nothing useful.

### 3.5 SIGHUP not forwarded to child processes on shell exit

**Where it bites:** Terminal disconnect (closing the window, ssh disconnect) sends SIGHUP to the shell. Bash forwards SIGHUP to all jobs unless `disown`-ed or `nohup`-prefixed. Stash currently has no equivalent — the shell exits, child processes are cleaned up via `CleanupTrackedProcesses`, but only those tracked through `process.spawn`. Shell-mode children launched via `RunPassthroughCommand` are _not_ tracked the same way (need to verify) and may either leak or die abruptly without SIGHUP.

**What it needs:** SIGHUP handler in shell mode that forwards SIGHUP to all known children (foreground + background jobs once 3.4 lands), then exits. A `disown` built-in to opt jobs out.

**Severity: medium-blocker.** Most users won't notice immediately, but `nohup ./long-job.sh &` followed by closing the terminal currently has unpredictable behaviour.

---

## 4. Important Gaps — Daily Friction

These won't break the system but you'll trip over them within hours of switching:

### 4.1 No `source` / `.` built-in

`source ~/.cargo/env`, `source $(asdf which something)`, `. /opt/intel/oneapi/setvars.sh` — all of these are Bash idioms used by toolchain installers. There is no equivalent in stash. `import` exists but it's namespace-scoped (`import x from "./module.stash"`) and can only import declared exports — it does **not** evaluate a script's side effects in the current scope.

**What it needs:** A shell-sugar `source <path>` (and `.` alias) that reads a `.stash` file, evaluates it against the current REPL VM (preserving globals/env mutations), and returns. This is roughly what `RcFileLoader.Load` already does — extract it as a built-in.

**Severity: high friction.** Affects rust, node, python, asdf, conda, oneapi, ros, every "source this to get our env" SDK installer.

### 4.2 No `alias` built-in

`alias ll='ls -la'`, `alias gst='git status'` — every dotfiles repo has dozens. Without aliases, users must rewrite each as a Stash function:

```stash
fn ll() { $>(ls -la); }
```

…which works but is ceremonial, not interactive-friendly, and doesn't compose with shell-mode bare invocation. Aliases also need late expansion (the alias body is re-tokenised at the call site).

**What it needs:** An `alias name=value` shell-sugar built-in plus an `unalias` and `alias` (no args = list). Storage in a session-scoped dict on the VMContext. The classifier consults the alias table after PATH lookup but before "unknown identifier" diagnosis.

**Severity: high friction.** First thing every user will ask for.

### 4.3 No process substitution `<(cmd)` / `>(cmd)`

`diff <(sort a) <(sort b)`, `grep -f <(...) file` — common enough. Implementing requires `/dev/fd/N` (Linux) or named pipes. Mid-effort.

**Severity: medium.** Avoidable with temp files; users who want it want it badly.

### 4.4 No `umask` built-in

Affects file creation perms across the entire session. Toolchain scripts set it (`umask 077` for SSH keys). Trivial wrapper around the syscall.

**Severity: medium.** Security-sensitive scripts assume they can set it.

### 4.5 No heredocs (`<<EOF`)

For interactive use, less critical. For scripts you'd just use multi-line strings. But `cat <<EOF | sudo tee /etc/foo.conf` is a configuration idiom across every tutorial on the internet.

**Severity: medium.** Workaround exists (Stash multi-line strings + pipe).

### 4.6 `$VAR` env interpolation not supported

Only `${env.get("VAR")}` works. Every tutorial says `echo $HOME`. Users will type it constantly.

**Severity: medium friction** — easy to fix, mostly a parser/expander shim that maps `$WORD` → `${env.get("WORD") ?? ""}` outside of quoted strings.

### 4.7 No `&` background suffix even without full job control

Even before full bg/fg/jobs, a one-shot `cmd &` that detaches into a background process group (no terminal) and returns immediately would cover 80% of background usage. Easier than full job control because you don't need SIGTSTP, terminal transfer, or a jobs table you can reattach to.

**Severity: medium.** Often paired with §4.1 (you `source` something then run a server in bg).

---

## 5. Quality-of-Life Gaps (Nice to Have)

- `export VAR=value` shell-sugar (current: `env.set("VAR", "value")`)
- `unset VAR` for env vars (current: `env.unset("VAR")`; the `unset` keyword is for Stash bindings)
- `which` / `type` / `command` built-ins
- `time cmd` built-in
- `read` (read a line into a variable from stdin)
- `trap` (signal handlers — `signal.on` exists but isn't shell-shaped)
- `eval` (would need careful design — Stash has no runtime `eval`; could be source-string compile + run, the same primitive `source` would use)
- `set -e`, `set -x`, `set -u` (strict modes — Stash already errors aggressively, but `-x` for trace is genuinely useful)
- `declare -i`, `local`, `readonly` — let-bindings cover this
- `case`/`esac` — `match` covers this in scripts; not needed at the prompt
- Brace-arithmetic `$((x+1))` — `${x+1}` already does it via Stash expressions

---

## 6. Concrete Risk Assessment for Switching Now on Arch

| Hazard                                       | Risk     | Mitigation                                                                                                                                         |
| -------------------------------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pacman / systemd / cron breakage             | None     | They use `/bin/sh`, not `$SHELL`                                                                                                                   |
| Cannot log into TTY                          | Low      | `agetty` exec's `-stash`; falls into REPL even without RC. PATH may be broken. Recoverable from another TTY                                        |
| Cannot log into graphical session            | **High** | Most DMs do `$SHELL -l -c '<session>'`. Without §3.1 + §3.3 this fails silently to xinit. Stay on bash for `~/.xinitrc` is no help — DMs ignore it |
| `ssh user@host cmd` and `scp`/`rsync` broken | **High** | All three use `$SHELL -c`. Affects you immediately if you SSH into the machine                                                                     |
| `git commit` editor invocation               | Medium   | `git` does `$SHELL -c "$EDITOR file"`. Often single-word; safe-ish                                                                                 |
| Sudo password prompt fails                   | Low      | sudo doesn't go through user shell                                                                                                                 |
| `$EDITOR` invocations from CLI tools         | Low      | Most call execvp directly, not via shell                                                                                                           |
| Vim Ctrl-Z to background                     | Medium   | No job control. Ctrl-Z stops vim, terminal hangs                                                                                                   |
| Sourcing rust/node/asdf env files            | Medium   | `source` not implemented; manual `env.set` calls needed                                                                                            |
| Paste a tutorial command with `$VAR`         | Medium   | Won't expand; user has to convert syntax                                                                                                           |
| Recovery from broken stash startup           | Low      | Always boot to single-user / log in as root from another TTY / use install media                                                                   |

---

## 7. Recommended Path to "Daily-Driver Ready"

In dependency order:

1. **§3.1 (`-c` shell mode)** — single highest-value fix. Unlocks ssh-remote, scp, rsync, display managers (partially), git editor. Maybe one week. Should have a precedent: route through the same `ShellRunner.Run` used by REPL, with a slightly different lifecycle (no prompt, no completion, exit-on-EOF, exit code from last command).
2. **§3.2 (login-shell detection + profile)** — low complexity, high correctness. Argv[0] check, `--login` flag, define `/etc/stash/profile.stash` and `~/.config/stash/profile.stash` paths, document load order. Could land same week as §3.1.
3. **§3.3 (`exec` shell sugar)** — trivial wrapper around existing `process.exec`. Few hours.
4. **§4.1 (`source` built-in)** — extract from `RcFileLoader`. Few hours.
5. **§4.2 (alias support)** — small parser change in `ShellLineClassifier` + a session table. One week including tests.
6. **§4.6 (`$VAR` expansion)** — one-pass shim in `ArgExpander`, gated to outside quoted strings. Few days.
7. **§3.5 (SIGHUP forwarding)** — POSIX signal handler + child tracking. Few days.
8. **§3.4 (job control)** — the big one. Multi-week. Tackle last because you can survive without it via tmux. Phase: (a) `&` background launch + `wait`; (b) Ctrl+Z + `fg`/`bg`/`jobs`; (c) terminal ownership transfer for TUI apps.

After 1-7, stash is _daily-able for a power user who knows the gaps_. After 8 it's _daily-able for an unsuspecting user_.

---

## 8. Decision

**Recommendation:** Do not `chsh -s` on a single-user machine until §3.1, §3.2, and §3.3 ship at minimum. The display-manager and SSH-remote-command failures are the showstoppers — both are silent, both happen at exactly the wrong moment (when you're locked out or trying to recover).

**Safer interim:** Run `stash --shell` from inside your existing bash terminal. Get the daily-driver feel. Build muscle memory for the gaps. Treat each thing that frustrates you as a vote for which spec to promote out of backlog next.

**Followups this analysis suggests:**

1. New spec: `Login Shell Mode — argv[0], -l flag, profile loading.md` (§3.2)
2. New spec: `Shell Mode for -c and Stdin — POSIX -c semantics.md` (§3.1)
3. New spec: `Shell Sugar — exec, source, alias, export.md` (§3.3, §4.1, §4.2, plus future `export`)
4. New spec: `Job Control — Background, Foreground, Suspend, Resume.md` (§3.4 — major design effort)
5. Smaller specs for `$VAR` expansion (§4.6), SIGHUP forwarding (§3.5), `umask`/`which`/`type`/`time`/`read` built-ins (§5)

Each of these would warrant its own kanban entry with grammar, semantics, cross-platform notes, error reference, and test plan — same template as the existing shell-mode docs.

---

## 9. Decision Log

| Date       | Decision                                                                             | Rationale                                                                                                |
| ---------- | ------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------- |
| 2026-05-01 | Frame the question as "five blockers + seven QoL gaps" instead of feature-by-feature | Forces ranking by impact-to-daily-use, not just by complexity                                            |
| 2026-05-01 | Treat `-c` shell-mode as the single highest-priority blocker                         | Unblocks SSH remote commands, scp/rsync, display managers — a much larger surface than job control would |
| 2026-05-01 | Job control deferred to last in the recommended path                                 | Largest implementation cost; tmux is a workable interim                                                  |
| 2026-05-01 | Recommend `stash --shell` inside bash as the safer interim posture                   | Lets you accumulate real-world feedback without locking yourself out                                     |
