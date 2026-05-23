# Stdlib Namespace Audit — Mixed-Responsibility Cleanup

> **Status:** Backlog (analysis / brainstorming)
> **Created:** 2026-05-01
> **Purpose:** Sister spec to _Process Namespace Decomposition_. Audit every
> stdlib namespace for the same kind of mixed-responsibility smell and decide
> which deserve splitting **now** rather than later, applying the same
> principles already settled there:
>
> 1. No nested namespaces (language constraint).
> 2. Repatriate misplaced functions into existing homes when a sibling already
>    fits.
> 3. Spin off a new namespace only when the new bucket is genuinely cohesive
>    _and_ the old bucket is genuinely overloaded.
> 4. Avoid creating tiny namespaces just for theoretical purity — Stash
>    tolerates small namespaces, but only when their boundary is principled.

---

## 1. Methodology

For every namespace in `Stash.Stdlib/BuiltIns/*BuiltIns.cs`, I asked:

- **Does it answer one question, or several?** A namespace that mixes
  "manipulate X", "ask facts about X", and "react to X events" is a smell.
- **Does it mix abstraction levels?** High-level helpers next to low-level
  primitives in the same bucket cause LSP-completion noise for users who
  only need one tier.
- **Does the smell impose a real cost on users?** A 50-function namespace
  isn't automatically bad if all 50 functions answer the same question.
- **Is the proposed new home itself principled?** Splitting just to make the
  number smaller is over-engineering.

Function counts (functions registered, not including constants/structs/enums):

| Namespace | Funcs | Smell?                                                  |
| --------- | ----: | ------------------------------------------------------- |
| buf       |    24 | No                                                      |
| arr       |    46 | **Yes** (Tier 2)                                        |
| str       |    40 | **Yes** (Tier 1)                                        |
| net       |    38 | **Yes** (Tier 1, biggest offender)                      |
| fs        |    37 | **Yes** (Tier 2)                                        |
| time      |    31 | No (cohesive)                                           |
| process   |    28 | Already specced — see _Process Namespace Decomposition_ |
| math      |    23 | No                                                      |
| dict      |    21 | No                                                      |
| env       |    20 | No (already cleaned by process spec)                    |
| prompt    |    17 | No                                                      |
| crypto    |    16 | No                                                      |
| sftp/ssh  |  15/8 | No (network protocols, cohesive each)                   |
| conv      |    13 | **Yes** (Tier 2, mild)                                  |
| term/sys  |    12 | sys: **Yes** (Tier 1, signal cluster)                   |
| Others    |   ≤11 | No                                                      |

Actual function names per top namespace are in the appendix (§7).

---

## 2. Tier 1 — Worth Doing Now

Three changes give meaningful relief, set good precedent, and align with
decisions already made in the _Process Namespace Decomposition_ spec.

### 2.1 Spin off `re.*` from `str.*` (regex)

**Today:** `str.match`, `str.matchAll`, `str.isMatch`, `str.replaceRegex`,
`str.capture`, `str.captureAll` — six functions all about regular
expressions, sitting alongside 34 string-manipulation functions.

**Smell:** Regex is conceptually distinct from "string manipulation". Every
language with a sane regex story (Python `re`, JavaScript `RegExp`, Go
`regexp`, Rust `regex`) treats it as its own module. A user scanning `str.*`
completions for `replace` shouldn't have to wade past `replaceRegex`,
`captureAll`, etc.

**Move:**

| Current            | New             |
| ------------------ | --------------- |
| `str.match`        | `re.match`      |
| `str.matchAll`     | `re.matchAll`   |
| `str.isMatch`      | `re.test`       |
| `str.replaceRegex` | `re.replace`    |
| `str.capture`      | `re.capture`    |
| `str.captureAll`   | `re.captureAll` |

`str.replace` and `str.replaceAll` (literal replacements) **stay in `str.*`** —
they are not regex.

Rename `isMatch` → `test` (matches the universal `RegExp.test()` idiom).
Rename `replaceRegex` → `replace` (no longer needs disambiguation now that
it lives in `re.*`).

**Upside:**

- `str.*` shrinks to 34 cohesive string-manipulation functions.
- `re.*` is a small, single-purpose namespace (6 functions) but its boundary
  is universally understood — every developer knows what "regex" means.
- Sets up a clean future: pattern compilation (`re.compile(pattern)` returning
  a reusable `Regex` struct) lands here naturally without further restructuring.

**Downside:** Breaking rename across all callers. Mitigated by the same
deprecation-alias-then-remove path used in the process spec.

---

### 2.2 Decompose `net.*` — split out `ws.*`, `tcp.*`, `udp.*` and `dns.*`

**Today:** `net.*` is the largest non-process namespace (38 functions) and
covers six unrelated concerns:

| Concern                    | Functions                                                                                         | Count |
| -------------------------- | ------------------------------------------------------------------------------------------------- | ----: |
| IP / subnet utilities      | `subnetInfo`, `mask`, `network`, `broadcast`, `hostCount`                                         |     5 |
| DNS                        | `resolve`, `resolveAll`, `reverseLookup`, `resolveMx`, `resolveTxt`                               |     5 |
| Network info / diagnostics | `ping`, `isPortOpen`, `interfaces`, `interface`                                                   |     4 |
| TCP sockets (sync + async) | `tcpConnect`, `tcpSend`, `tcpRecv`, `tcpClose`, `tcpListen`, `tcpConnectAsync`, `tcpSendAsync`, … |    11 |
| UDP                        | `udpSend`, `udpRecv`                                                                              |     2 |
| WebSocket                  | `wsConnect`, `wsSend`, `wsSendBinary`, `wsRecv`, `wsClose`, `wsState`, `wsIsOpen`                 |     7 |

**Smell:** WebSocket has nothing to do with IP-level networking — it's an
application-layer protocol that happens to ride TCP. TCP and UDP are
transport-layer primitives that warrant their own surface. The `wsXxx`,
`tcpXxx`, `udpXxx` prefixes that fill the function names are a textbook
"this should be a namespace" signal — the prefix _is_ a namespace, just one
the users have to spell out every time.

**Move (recommended):**

| Current                                                                    | New                                                |
| -------------------------------------------------------------------------- | -------------------------------------------------- |
| `net.tcp*`                                                                 | `tcp.*` (drop the `tcp` prefix from each function) |
| `net.udp*`                                                                 | `udp.*` (drop the `udp` prefix)                    |
| `net.ws*`                                                                  | `ws.*` (drop the `ws` prefix)                      |
| `net.subnet*`, `net.mask`, `net.network`, `net.broadcast`, `net.hostCount` | stay in `net.*`                                    |
| `net.resolve*`, `net.reverseLookup`                                        | move to `dns.*`                                    |
| `net.ping`, `net.isPortOpen`, `net.interfaces`, `net.interface`            | stay in `net.*`                                    |

Function renames after de-prefixing:

- `tcp.connect`, `tcp.send`, `tcp.recv`, `tcp.close`, `tcp.listen`,
  `tcp.connectAsync`, …
- `udp.send`, `udp.recv`
- `ws.connect`, `ws.send`, `ws.sendBinary`, `ws.recv`, `ws.close`,
  `ws.state`, `ws.isOpen`

**Upside:**

- `net.*` shrinks to ~14 cohesive "internet plumbing facts" functions.
- `tcp.*`, `udp.*`, `ws.*` each have one clear purpose; users importing or
  reading code immediately see which transport is in play.
- Eliminates the `net.tcpFooAsync` / `net.tcpFoo` symmetry duplication — it
  becomes `tcp.fooAsync` / `tcp.foo`, which is what we wanted in the first
  place.
- The `_async` suffix family becomes a candidate for cleanup too (sync vs.
  async siblings within one namespace), but that is its own design discussion
  and not part of this spec.

**Downside:**

- Three new namespaces in one stroke. We accept this because each has a
  rock-solid, universally-understood boundary (TCP / UDP / WebSocket are
  industry-standard concept names).
- Larger breaking surface than the regex split.

**Alternative considered (rejected):** "split out only `ws.*`, keep TCP and
UDP under `net.*`." Rejected because TCP alone is 11 functions — bigger than
several existing namespaces — and the `tcp` prefix proves the case for a
dedicated home.

---

### 2.3 New `signal.*` namespace — close the loop on the `Signal` enum

**Today:** Signal handling is split — `process.signal()` _sends_ signals
(now accepting the global `Signal` enum from the process-decomposition
spec), but `sys.onSignal()` and `sys.offSignal()` _receive_ them.
`sys.*` otherwise contains pure system-info read-only functions
(`cpuCount`, `totalMemory`, `loadAvg`, `diskUsage`, …). The signal
event-handler pair is the only "register a callback" API in `sys.*`, and
it shares no concepts with the rest of the namespace.

**Move:**

| Current         | New          |
| --------------- | ------------ |
| `sys.onSignal`  | `signal.on`  |
| `sys.offSignal` | `signal.off` |

Optionally migrate `process.signal(handle, sig)` → `signal.send(handle, sig)`
to put **all** signal verbs together. This is more ambitious — see §6
discussion item.

**Upside:**

- `signal.*` becomes the natural, principled home for the `Signal` enum we
  already added globally. The vocabulary (`Signal.Term`) and the verbs
  (`signal.on`, `signal.off`, optionally `signal.send`) finally live next to
  each other.
- `sys.*` shrinks to 10 cohesive read-only system-info functions.
- Drops the `Signal` suffix that bloated the function names.
- Tiny namespace (2 functions) but with a perfectly clear boundary —
  signals are a finite, well-defined OS concept.

**Downside:**

- A 2-function namespace is on the small side. Counter: `args.*` is also
  small; `pkg.*` has 4 functions. Smallness alone is not a problem when the
  boundary is principled.

---

## 3. Tier 2 — Worth Considering, but Less Urgent

These have real smells but the cost/benefit is closer. Recommend listing
them in the spec for visibility, but not committing to them in the same
release as Tier 1.

### 3.1 `arr.*` — typed-array reflection cluster

`arr.typed`, `arr.untyped`, `arr.elementType`, `arr.new` form a coherent
"typed array reflection" cluster — they're about array _types_, not array
_operations_. Every other `arr.*` function takes an array and does something
with its values; these four are about the array's element-type metadata.

**Options:**

- (a) Leave alone. They share the noun "array" with everything else in `arr.*`.
- (b) Move to `arr.*` siblings with naming that makes the role clearer:
  rename `new` (currently shadows the `new` keyword in user heads) to
  `allocate` or `create`. No namespace move.
- (c) Spin off a `typed.*` namespace. Probably overkill given the count.

**Recommendation:** (b). Don't move; just rename `arr.new` to `arr.create`
or `arr.allocate` to remove the visual collision with the `new` keyword.

### 3.2 `arr.par*` — parallel iteration cluster

`arr.parMap`, `arr.parFilter`, `arr.parForEach` are the only parallelism
primitives in the stdlib. They live in `arr.*` because that's where their
serial counterparts live, which is ergonomically convenient — but they
introduce thread-pool scheduling concerns that no other `arr.*` function
has.

**Options:**

- (a) Leave alone — the ergonomic argument wins; users naturally reach for
  `arr.parMap` after `arr.map`.
- (b) Move to `task.*` (which already exists for async work, 11 functions)
  as `task.parMap(arr, fn)` etc. Conceptually correct but ergonomically
  worse — calling `task.parMap(myArr, fn)` instead of `myArr.parMap(fn)`
  via UFCS loses the receiver-first reading order.

**Recommendation:** (a). The UFCS ergonomics outweigh the conceptual purity.

### 3.3 `fs.*` — permissions cluster

`fs.readable`, `fs.writable`, `fs.executable`, `fs.getPermissions`,
`fs.setPermissions`, `fs.setReadOnly`, `fs.chown`, `fs.setExecutable` form
a coherent 8-function permissions cluster.

**Options:**

- (a) Leave alone. Permissions are a property of files, files live in `fs.*`,
  done.
- (b) Move to `fs.*`-siblings under a name like `perm.*`. Rejected: small,
  cross-platform-fragile (Windows ACLs vs POSIX modes), and tightly coupled
  to the file lifecycle.

**Recommendation:** (a). Cluster exists but isn't worth a split.

### 3.4 `conv.*` — three concerns in one namespace

`conv.*` has 13 functions across three sub-concerns:

1. **Type coercion** (`toStr`, `toInt`, `toFloat`, `toBool`, `toByte`) — 5
2. **Number-base conversion** (`toHex`, `toOct`, `toBin`, `fromHex`,
   `fromOct`, `fromBin`) — 6
3. **Character codes** (`charCode`, `fromCharCode`) — 2

**Options:**

- (a) Leave alone. All "conversion" of one form or another.
- (b) Move number-base helpers to `math.*` or a new `num.*`. Move character
  codes to `str.*`.
- (c) Move character codes to `str.*` only, leave the rest.

**Recommendation:** (c). `str.charCode` and `str.fromCharCode` read
naturally and reduce `conv.*` from 13 → 11. The number-base split is too
invasive for too little benefit.

### 3.5 `time.*` — duration helpers

`time.seconds`, `time.minutes`, `time.hours`, `time.days`, `time.weeks` are
duration constructors (return durations as ints/StashDateTime offsets).
Distinct from datetime construction (`time.date`, `time.now`, …) and
arithmetic (`time.add`, `time.diff`, `time.toTimezone`).

**Recommendation:** Leave alone. The five duration helpers are tightly
coupled to the rest of `time.*` (used as inputs to `time.add` etc.) and a
`duration.*` namespace would just shuffle problems around.

---

## 4. Tier 3 — Don't Touch

For the record:

- **`buf.*`** — 24 functions, all about binary buffer manipulation. The
  read/write float/double family looks repetitive but is a coherent binary
  interface.
- **`str.*` classification (`isDigit`, `isAlpha`, …)** — tightly coupled to
  string semantics; small enough not to bother.
- **`math.*`** — completely cohesive.
- **`dict.*`** — completely cohesive.
- **`crypto.*`** — acceptable mix of hashing, HMAC, encryption, key
  generation; all crypto.
- **`time.*`** — see §3.5.
- **`fs.*`** beyond permissions — see §3.3.
- **`prompt.*`, `term.*`, `args.*`, `pkg.*`, `test.*`** — already
  single-purpose.

---

## 5. Recommended Scope for the First PR

Tier 1 only:

1. **Regex split:** new `re.*` namespace.
2. **Network decomposition:** new `tcp.*`, `udp.*`, `ws.*` namespaces.
3. **Signal split:** new `signal.*` namespace, taking `sys.onSignal/offSignal`
   (and optionally `process.signal` — see §6).

All Tier 2 items become their own follow-up specs if and when we decide to
pursue them — they should not bloat the first cleanup wave.

---

## 6. Open Questions

1. **`signal.send`?** Should `process.signal(handle, sig)` move to
   `signal.send(handle, sig)`? Pro: puts every signal verb in one place.
   Con: the function operates on a `Process` handle, so there's a real
   argument for keeping it in `process.*`. (Mirror question for the original
   process spec — symmetry with `process.kill` favors keeping it in
   `process.*`.) Recommendation: keep in `process.*`. `signal.*` owns the
   _event-handler_ role; `process.*` owns _acting on a process_, including
   sending signals to it.
2. **`re.compile` and a `Regex` struct?** Pre-compiling a pattern for reuse
   is the standard regex idiom in most languages. It's not in scope here
   because Stash's current regex API is stateless. Recommend filing a
   separate spec to add `re.compile(pattern) → Regex` once `re.*` exists.
3. **What to do about the `*Async` family duplication?** Many `tcp.*`
   functions exist in both sync and async forms (`tcpConnect`/`tcpConnectAsync`).
   Out of scope for this spec — list as a follow-up. The decomposition into
   `tcp.*` makes it visually obvious how big the duplication is, which will
   pressure us to address it sooner rather than later.

---

## 7. Migration & Deprecation Strategy

Identical to the _Process Namespace Decomposition_ spec:

1. Land new namespaces in release N.
2. Old names continue to work as deprecated aliases in N, emitting the
   same `SA08xx: '<old>' is deprecated — use '<new>' instead` diagnostic
   that the process spec already introduces.
3. Update bundled examples and `@stash/*` packages in N.
4. Remove deprecated aliases in N+2.

This spec piggybacks on the deprecation infrastructure being built for the
process spec — no new analysis machinery required.

---

## 8. Decision Log

| Date       | Decision                                                                                                         | By        |
| ---------- | ---------------------------------------------------------------------------------------------------------------- | --------- |
| 2026-05-01 | Spec drafted. Tier 1 (re, tcp/udp/ws/dns, signal) recommended; Tier 2 catalogued; Tier 3 explicitly not touched. | Architect |

> Awaiting user input on §6 open questions before promotion to `1-todo/`.

---

## Appendix — Function Inventory (top namespaces)

For reference; not part of the design.

```
arr (46)    push pop peek insert removeAt remove clear contains includes
            indexOf lastIndexOf slice concat join reverse sort map filter
            forEach parMap parFilter parForEach find reduce unique any every
            flat flatMap findIndex count sortBy groupBy sum min max zip
            chunk shuffle take drop partition typed untyped elementType new

str (40)    upper lower trim trimStart trimEnd contains startsWith endsWith
            indexOf lastIndexOf substring replace replaceAll split repeat
            reverse chars padStart padEnd isDigit isAlpha isAlphaNum isUpper
            isLower isEmpty match matchAll isMatch replaceRegex capture
            captureAll count format capitalize title lines words truncate
            slug wrap

net (38)    subnetInfo mask network broadcast hostCount resolve resolveAll
            reverseLookup ping isPortOpen interfaces interface tcpConnect
            tcpSend tcpRecv tcpClose tcpListen udpSend udpRecv resolveMx
            resolveTxt wsConnect wsSend wsSendBinary wsRecv wsClose wsState
            wsIsOpen tcpConnectAsync tcpSendAsync tcpSendBytesAsync
            tcpRecvAsync tcpRecvBytesAsync tcpCloseAsync tcpIsOpen tcpState
            tcpListenAsync tcpServerClose

fs (37)     readFile writeFile exists dirExists pathExists createDir delete
            copy move size listDir appendFile readLines glob isFile isDir
            isSymlink tempFile tempDir modifiedAt walk readable writable
            executable createFile symlink stat getPermissions setPermissions
            setReadOnly chown setExecutable watch unwatch readBytes
            writeBytes appendBytes

sys (12)    cpuCount totalMemory freeMemory uptime loadAvg diskUsage pid
            tempDir networkInterfaces which onSignal offSignal

conv (13)   toStr toInt toFloat toBool toByte toHex toOct toBin fromHex
            fromOct fromBin charCode fromCharCode
```
