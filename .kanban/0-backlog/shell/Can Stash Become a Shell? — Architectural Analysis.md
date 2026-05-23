## Can Stash Become a Shell? — Architectural Analysis

This is a fascinating question, and the answer is **yes, but it's a significant undertaking**. Here's a breakdown of what exists, what's missing, and the architectural challenges.

---

### What Stash Already Has (Shell-Adjacent Features)

Your instinct that it's "not set up in the slightest" is partially right — but you're actually closer than you might think in some areas:

| Feature           | Shell Needs                 | Stash Status                                     |
| ----------------- | --------------------------- | ------------------------------------------------ |
| Command execution | Run external programs       | **Has it** — `$(cmd)` and `$>(cmd)`              |
| Pipes             | `cmd1 \| cmd2`              | **Has it** — but buffered, not streaming         |
| Redirections      | `>`, `>>`, `2>`             | **Has it** — `>`, `>>`, `2>`, `&>`               |
| Environment vars  | Read/write env              | **Has it** — `env.get()`, `env.set()`            |
| Tilde expansion   | `~/path`                    | **Has it**                                       |
| Interpolation     | Variables in commands       | **Has it** — `$(echo ${name})`                   |
| REPL              | Interactive loop            | **Has it** — with readline-like editing, history |
| Error recovery    | Don't crash on bad input    | **Has it** — REPL continues after errors         |
| Signal handling   | Ctrl+C cleanup              | **Has it** — graceful child process cleanup      |
| Elevation         | sudo/doas                   | **Has it** — `elevate { }` block                 |
| Scripting         | Variables, functions, loops | **Way beyond** what bash offers                  |

---

### The Fundamental Architectural Gaps

#### 1. **Bare Command Execution (The Big One)**

This is the single largest gap and it's a parser-level problem.

- **Bash:** You type `ls -la` and it runs. Every unrecognized word is a potential command.
- **Stash:** You type `ls -la` and the parser sees identifier `ls`, minus operator, identifier `la`. It's a syntax error or a subtraction expression.

The `$()` wrapper exists precisely because the parser needs to distinguish "this is a command" from "this is a Stash expression." In a shell, **everything is a command by default** and programming constructs are the special case. In Stash, it's the inverse.

**What it would take:** A dual-mode parser or a "shell mode" where the default interpretation of a line changes. When you type `git status`, the shell mode would recognize `git` is not a Stash keyword or defined variable, and treat the line as a command invocation. This is a non-trivial change to the lexer and parser — you'd essentially need a second parsing grammar for "bare command mode."

#### 2. **Streaming Pipes (Currently Buffered)**

- **Bash:** `cat huge_file | grep pattern | head -5` — data streams between processes concurrently. `head -5` closes its input after 5 lines, allowing `cat` to terminate early.
- **Stash:** The left side of `|` runs to completion, its entire stdout is buffered as a string, then passed as stdin to the right side.

For interactive shell use, buffered pipes are a dealbreaker. Imagine `tail -f /var/log/syslog | grep error` — it would never display anything because `tail -f` never finishes.

**What it would take:** Replace the current pipe implementation with OS-level pipe file descriptors (`pipe(2)` syscall). Each command in a pipeline would need to be launched with its stdout connected directly to the next command's stdin via kernel pipes, allowing true streaming. This is a rewrite of the pipe execution in `Interpreter.Commands.cs`.

#### 3. **Working Directory (`cd`)**

- **Bash:** `cd /tmp` changes the process's current directory. All subsequent commands run there.
- **Stash:** No `cd` built-in. The interpreter process stays wherever it was launched.

This sounds trivial but it's architecturally important — `cd` must be a **shell built-in** (not an external command) because changing the working directory only affects the current process. External `cd` would be useless.

**What it would take:** A shell built-in `cd` that calls `System.Environment.CurrentDirectory = newPath` and a set of other shell built-ins (`pwd`, `pushd`, `popd`). Relatively straightforward.

#### 4. **Job Control (Background/Foreground)**

- **Bash:** `sleep 100 &`, `jobs`, `fg %1`, `bg`, Ctrl+Z to suspend
- **Stash:** No job control at all. Commands either block or are fire-and-forget via `process.spawn()`.

Job control requires:

- Process group management (setpgid/tcsetpgrp)
- Terminal ownership transfer (foreground vs background)
- SIGTSTP (Ctrl+Z) handling to suspend the foreground process
- SIGCONT to resume

**What it would take:** Significant low-level POSIX work. Need P/Invoke for `setpgid()`, `tcsetpgrp()`, `tcgetpgrp()`, and signal handlers for SIGTSTP, SIGCONT, SIGCHLD. This is one of the harder parts — it's the kind of thing that works differently on every platform and is the reason most "we'll add a shell" projects stall.

#### 5. **Glob Expansion**

- **Bash:** `rm *.tmp`, `ls src/**/*.cs`
- **Stash:** No globbing. `*.tmp` would be a syntax error (multiply operator on a dot expression).

**What it would take:** A glob expansion phase in the command execution pipeline. Before passing arguments to the external command, expand glob patterns using `Directory.GetFiles()` / `Directory.GetDirectories()` with pattern matching. Moderate effort — well-defined problem.

#### 6. **Tab Completion**

- **Bash:** Tab-completes commands, file paths, arguments, and supports programmable completion.
- **Stash:** The `LineEditor` has history and cursor movement but no tab completion.

**What it would take:** Hook into `LineEditor` for the Tab key. At minimum: file path completion and command name completion (scan PATH). Ideally: programmable completion for Stash functions, variables, and namespace members. This is a significant UX feature.

#### 7. **Startup Configuration**

- **Bash:** Sources `~/.bashrc` on interactive start, `~/.bash_profile` on login.
- **Stash:** No RC file mechanism.

**What it would take:** On REPL startup, check for `~/.stashrc` (or `~/.config/stash/init.stash`) and execute it if found. The infrastructure is there — the interpreter can already `Interpret(statements)` from a file. You just need to load it on startup. Low effort.

#### 8. **Prompt Customization**

- **Bash:** `PS1='\u@\h:\w\$ '` with escape sequences for user, hostname, working directory, etc.
- **Stash:** Hardcoded `stash> ` prompt.

**What it would take:** A `PROMPT` variable or function that gets evaluated before each REPL line. Could be a Stash function that returns a string, giving users full language power for their prompt. Moderate effort.

---

### The Architecture Spectrum: Three Approaches

#### Approach A: "Shell Mode" (Minimal — Add Shell-Like Features to the Existing Language)

Keep Stash as a programming language but make it more comfortable as a login shell:

- Add `cd`, `pwd`, `pushd`/`popd` built-ins
- Add RC file loading (`~/.stashrc`)
- Add prompt customization
- Add tab completion (files + commands)
- Add glob expansion in `$()` commands
- Keep the `$()` syntax for commands

**Effort:** Moderate. 2-3 weeks of focused work.
**Result:** A programmable language you _can_ use as a shell, but commands still need `$()`. Think like using Python as a shell with `os.system()` — functional but not natural.

#### Approach B: "Dual-Mode Parser" (Medium — Bare Commands in REPL)

Everything from Approach A, plus:

- In REPL/interactive mode, lines that don't parse as valid Stash are tried as bare commands
- E.g., type `git status` → parser fails → shell fallback → execute `git status`
- Script files still use `$()` syntax (keeps the language clean)
- Streaming pipes for bare command mode

**Effort:** Significant. The "try-parse-then-fallback" approach is tricky — you need to handle ambiguity (is `test -f file` the Stash `test` keyword or the test command?). Maybe 1-2 months.
**Result:** Feels like a shell in interactive use, still a clean language in scripts. This is what **Nushell** and **Oil Shell** roughly do — they have a "command mode" and an "expression mode."

#### Approach C: "Full Shell" (Major — First-Class Shell Language)

Everything from A and B, plus:

- Bare commands work everywhere (scripts too), not just REPL
- Grammar-level distinction between command mode and expression mode
- Streaming pipes
- Full job control (bg/fg/Ctrl+Z)
- Process substitution (`<(cmd)`)
- Here-documents
- This would essentially make every line default to "command mode" unless it starts with a Stash keyword

**Effort:** Major. This is a fundamental rethink of the parser. You'd be building a new language grammar that has two modes woven together. Think 3-6 months of careful design and implementation.
**Result:** A true modern shell language. This is what **Elvish**, **Nushell**, and **Oil/Oils** are — purpose-built shell languages with real programming features.

---

### The Core Design Tension

The fundamental tension is this:

> In a **programming language**, `foo bar` is ambiguous (function call? two expressions? subtraction without spaces?).
> In a **shell**, `foo bar` is unambiguous — run `foo` with argument `bar`.

Bash "solves" this by making everything a string by default, which is why bash programming is such a nightmare. Stash solves it the other way — everything is a programming expression by default, which is why it's a great language but not a natural shell.

The languages that have successfully bridged this gap (Nushell, Elvish, Oil) all use some form of **context-dependent parsing** — they know when they're in "command position" vs "expression position" based on grammar rules. For example:

```
# Nushell approach:
ls | where size > 1mb          # command mode: ls, where are commands
let x = 5 + 3                  # expression mode: 5, +, 3 are expressions
```

This is achievable for Stash, but it would be the biggest architectural change since the language was created. The lexer and parser would need a significant rethink.

---

### My Recommendation

If this idea excites you, I'd suggest **Approach B** as the sweet spot:

1. Start with the easy wins (Approach A features — `cd`, RC files, prompt, tab completion, globs)
2. Then prototype the "bare command fallback" in the REPL only
3. See how it feels. If the ambiguity issues are manageable, expand to scripts

This path lets you iterate without committing to a full parser rewrite upfront. The REPL already has the infrastructure (persistent interpreter, error recovery, LineEditor) — you'd be building on solid foundations rather than starting over.
