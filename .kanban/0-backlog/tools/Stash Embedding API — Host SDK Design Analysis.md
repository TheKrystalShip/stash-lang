# Stash Embedding API — Host SDK Design Analysis

> **Status:** Implementation-ready (backlog → ready to be promoted to `1-todo` when scheduled)
> **Created:** 2026-05-01
> **Last updated:** 2026-05-01 — added §13 thread-safety audit and §14 decisions resolving the open questions from §12.
> **Type:** API design study — gathers gaps, proposes a Host SDK shape, captures committed design decisions.
> **Driving use case:** ASP.NET Core web API embeds Stash; Stash IS the CLI tool (game-server manager); web requests trigger Stash functions that return JSON-shaped data to a React frontend.

---

## 1. The current embedding surface in one paragraph

There is a public `StashEngine` facade ([Stash.Bytecode/StashEngine.cs](Stash.Bytecode/StashEngine.cs)) that exposes `Run(source)`, `Evaluate(expr)`, `Compile(source) → StashScript`, `Run(StashScript)`, `RunFile(path)`, `AddStdlibProvider(IStdlibProvider)`, plus a `StepLimit` and a small `ExecutionResult { Value, Errors, Success }` return shape. Underneath, the real workhorses are public: `VirtualMachine`, `Compiler`, `BytecodeReader`, `StashValue`, `StashInstance`, `StashDictionary`, `IStashCallable`, `IStdlibProvider`, `NamespaceBuilder`, and `BuiltInFunction.DirectHandler`. `AnalysisEngine` is also public but pulls in reflection (so it's NAOT-hostile). DAP and LSP are not embeddable in NAOT for the same reason.

That's a workable foundation. It is **not** a host SDK. It's "the things the CLI happened to need, made `public` so embedders could reach in." The gap between that and what a serious .NET host wants is large but well-bounded.

---

## 2. The use case in one paragraph

A React admin dashboard talks to an ASP.NET Core API; the API embeds the Stash engine in-process; the engine runs game-server-management logic written in Stash; results flow back to the frontend as JSON. The Stash code IS the application — not a script that shells out to a separate CLI. The CLI binary and the web API are two front ends to the **same Stash codebase**, sharing the same functions, the same modules, the same package versions. (Confirmed by user: "Stash IS the CLI; the web API embeds the same Stash engine the CLI uses.")

That last fact is crucial. It means:

- Whatever embedding API we design must support **calling specific Stash functions** with structured arguments, not just running whole scripts.
- The Stash code must be **author-once, run-everywhere** — no per-host forking.
- The web API has to handle **concurrent requests** safely against the embedded engine.
- Data passed between the host and Stash is **JSON-shaped**: dicts, arrays, primitives, nested.

---

## 3. The eight gaps

Surveying the embedding surface against the use case surfaces eight concrete, addressable gaps. They roughly cluster into "hostability" (1–4) and "ergonomics" (5–8).

### Gap 1 — No call-a-function API

The current way to invoke a Stash-defined function from C# is to fish it out of `vm.Globals`, cast to `IStashCallable`, hand-build a `List<object?>`, and call `.Call(ctx, args)`. There is no `engine.Call("createServer", new { name = "foo", game = "factorio" })`. For a use case that's literally "the web API calls Stash functions per request", this is the most painful gap.

What's missing is a typed, ergonomic `Call`/`CallAsync` that:

- Looks up the function by name in the post-bootstrap globals (with a clear error if not found / not callable / wrong arity).
- Marshals C# arguments to `StashValue` automatically (see Gap 5).
- Marshals the return value back (Gap 5).
- Handles errors uniformly (Gap 4).
- Runs on a thread-pool thread (Gap 3).

This is **the** API the React/ASP.NET use case needs.

### Gap 2 — No concurrency model

`VirtualMachine` is **not thread-safe**:

- `Globals` is a plain `Dictionary<string, StashValue>`.
- `_globalSlots` array, `_constGlobals`, `_openUpvalues`, `_stack`, `_frames` are all instance state.
- `Output` / `ErrorOutput` / `Input` are single per-VM `TextWriter`/`TextReader` references.
- `ScriptArgs`, `ModuleLoader`, `TestHarness` are single fields.
- The cancellation token is **stored in the constructor** — you can't pass a different token per call.

Under ASP.NET Core, every request is on a thread-pool thread. Two concurrent requests against the same `StashEngine` will corrupt state. Today the only safe story is "one VM per request" — which forces the host to pay the full stdlib registration + globals dict construction cost on every request (measurable, not catastrophic — a few milliseconds in a typical case).

There is no built-in pool. There is no documented concurrency contract. There is no way to scope per-call output capture or per-call cancellation.

**The user wants a recommendation here.** See §5 for the proposed model.

### Gap 3 — No async / Task-based API

`Stash.Bytecode` is entirely synchronous. `engine.Run(...)` blocks the calling thread. ASP.NET handlers should not block thread-pool threads on synchronous CPU work, especially work that may also do blocking I/O (file reads, HTTP calls inside the Stash code). At the very least we need:

- `RunAsync(string source, CancellationToken)` that delegates to `Task.Run` internally.
- `CallAsync(string fnName, args, CancellationToken)` likewise.
- A per-call `CancellationToken` parameter (the current VM stores one in the constructor, which is wrong for a multi-call host).

A deeper "real" cooperative async model (Stash code that yields to other requests during a `time.sleep` or `http.get`) is **out of scope** — that's the unfinished `await` infrastructure, an entirely separate language design problem. The ask here is just `Task`-shaped wrappers and per-call cancellation.

### Gap 4 — Errors are stringly-typed

`ExecutionResult.Errors` is `IReadOnlyList<string>`. You get the message, not the type, not the source span, not the stack frame, not the original `RuntimeError`. For a web API that wants to:

- Map a `ValueError` to HTTP 400, an `IOError` to HTTP 503, an unhandled error to HTTP 500.
- Surface a structured error to the React frontend (`{ kind: "ValueError", message: "...", at: { file: "...", line: 12 } }`).
- Log the full Stash call stack to Application Insights / OpenTelemetry.

…strings are not enough. The information is all there inside `RuntimeError.CallStack` and `StashError.Type` — it's just thrown away by the time it crosses the embedding boundary.

### Gap 5 — No automatic JSON / POCO marshalling

Today, to pass a JSON-shaped object into Stash you must hand-build `StashDictionary` and `List<StashValue>` recursively. To get a result back out, you must walk the value tree and unbox each `StashValue` according to its `Tag`. The user's data model is JSON; ASP.NET's data model is `JsonElement` / `JsonDocument` / strongly-typed records.

We need:

- `StashValue.FromJson(JsonElement)` and `JsonElement.ToStashValue()`.
- A `StashValue.ToJson(Utf8JsonWriter)` that writes a Stash dict/array/primitive as JSON.
- Optionally: a generic POCO marshaller (`StashValue.From<T>(T obj)` / `value.As<T>()`) — though source generation makes this non-trivial in NAOT.

JSON is the lingua franca of web APIs. Without this bridge, every embedder reinvents the same recursive walker.

### Gap 6 — No DI integration

ASP.NET Core hosts expect `services.AddStash(options => { ... })` and `IStashEngine` injected into controllers. There is currently no `IServiceCollection` extension, no options pattern, no health checks, no `IHostedService` for managing pre-compiled scripts at startup, no logging integration (the `StashEngine` doesn't take an `ILogger`).

For an ASP.NET host, this is the difference between "third-party library" and "native .NET citizen."

### Gap 7 — No module loader story for embedded apps

`vm.ModuleLoader` is a single delegate (`Func<string, string?>`). The CLI sets it to read from disk. For an embedded web app, the source code may be:

- Embedded as resources in the assembly.
- Loaded from a database or content-management system.
- Mixed with `@stash/*` registry packages.
- Hot-reloaded from a watched directory in dev.

There's no `IStashModuleLoader` interface, no built-in providers (file system, embedded resource, registry), no composability. Every host writes the same loader.

### Gap 8 — Capabilities are coarse

`StashCapabilities` has 5 flags (FileSystem, Network, Process, Environment, Shell). For a web host that runs user-authored Stash logic on behalf of multiple tenants, that's not granular enough:

- "Allow `fs.read` but not `fs.write`."
- "Allow `http.get` to specific allowlisted hosts only."
- "Set a memory budget per call."
- "Forbid `$(...)` shell commands but allow `process.spawn`."

This isn't critical for the user's use case (their Stash code is first-party trusted code, not user-submitted), so I'm flagging it but not prioritizing it. It becomes important if the platform ever supports user-authored extensions.

---

## 4. What good looks like — proposed Host SDK shape

A real Host SDK should ship as a separate assembly — call it **`Stash.Hosting`** — that depends on `Stash.Bytecode` and `Stash.Stdlib` and exposes a tight, opinionated API designed for in-process use by .NET applications (Console, WPF, ASP.NET, Service Worker, anything).

The CLI itself (`Stash.Cli`) would migrate to use `Stash.Hosting` internally, eating its own dog food. This guarantees the Host SDK keeps pace with the language.

### 4.1 The core type

```csharp
namespace Stash.Hosting;

public interface IStashHost : IAsyncDisposable
{
    // Static analysis (optional pipeline step)
    Task<AnalysisReport> AnalyzeAsync(string source, CancellationToken ct = default);

    // Compile once
    Task<CompiledScript> CompileAsync(string source, ScriptOptions? options = null, CancellationToken ct = default);
    Task<CompiledScript> LoadAsync(string path, CancellationToken ct = default); // .stash or .stashc

    // Run whole scripts
    Task<StashResult<TResult>> RunAsync<TResult>(CompiledScript script, CancellationToken ct = default);
    Task<StashResult> RunAsync(CompiledScript script, CancellationToken ct = default);

    // Call specific functions
    Task<StashResult<TResult>> CallAsync<TResult>(string functionName, object? args = null, CancellationToken ct = default);
    Task<StashResult> CallAsync(string functionName, object? args = null, CancellationToken ct = default);

    // Inspection
    bool TryGetFunction(string name, out FunctionInfo info);
    IReadOnlyDictionary<string, FunctionInfo> Functions { get; }
}

public sealed record StashResult(
    bool Success,
    StashValue Value,
    IReadOnlyList<StashError> Errors,
    TimeSpan Duration,
    long StepsExecuted);

public sealed record StashResult<T>(
    bool Success,
    T? Value,
    IReadOnlyList<StashError> Errors,
    TimeSpan Duration,
    long StepsExecuted);

public sealed record StashError(
    string Kind,                   // "ValueError", "IOError", "ParseError", "Compilation", "Cancelled", ...
    string Message,
    SourceSpan? Span,
    IReadOnlyList<StackFrameInfo> CallStack);
```

Every method is async. Every method takes a per-call `CancellationToken`. Errors are structured. Results carry execution metadata (duration, steps) for telemetry.

The `CallAsync<T>(name, args)` method is the headline: pass a POCO or anonymous object as args, get a typed POCO back. Internally it marshals through the JSON bridge.

### 4.2 The DI integration

```csharp
// In ASP.NET startup:
services.AddStash(options =>
{
    options.Capabilities = StashCapabilities.FileSystem | StashCapabilities.Network;
    options.StepLimit = 10_000_000;
    options.DefaultTimeout = TimeSpan.FromSeconds(30);
    options.PoolSize = Environment.ProcessorCount * 2;
    options.AddProvider<MyCustomNamespaceProvider>();
    options.LoadScriptsFromEmbeddedResources(typeof(Program).Assembly, "scripts/");
});

// In a controller:
public class ServersController(IStashHost stash) : ControllerBase
{
    [HttpPost("/servers")]
    public async Task<IActionResult> Create([FromBody] CreateServerRequest req, CancellationToken ct)
    {
        var result = await stash.CallAsync<ServerInfo>("create_server", req, ct);
        return result.Success
            ? Ok(result.Value)
            : MapError(result.Errors[0]);
    }
}
```

The `AddStash` extension wires up:

- An `IStashHost` singleton (which manages the VM pool internally — see §5)
- `StashOptions` from configuration
- An `IHostedService` that pre-compiles scripts at startup (so requests don't pay parse cost)
- An optional `StashHealthCheck`
- Logging / OpenTelemetry hooks (each `CallAsync` becomes a span)

### 4.3 The marshalling bridge

```csharp
namespace Stash.Hosting.Marshalling;

public interface IStashMarshaller
{
    StashValue ToStash(object? value);
    StashValue ToStash<T>(T value);
    object? FromStash(StashValue value, Type targetType);
    T FromStash<T>(StashValue value);
}

public static class JsonStashBridge
{
    public static StashValue FromJsonElement(JsonElement element);
    public static void WriteJson(this StashValue value, Utf8JsonWriter writer);
    public static string ToJsonString(this StashValue value, JsonSerializerOptions? options = null);
}
```

Default marshaller uses `System.Text.Json` semantics:

- C# primitives → matching `StashValue` tag.
- `IDictionary<string, ...>` / anonymous objects / POCOs → `StashDictionary`.
- `IEnumerable` → Stash array (`List<StashValue>`).
- `null` → `StashValue.Null`.
- Reverse direction symmetric.

For high-performance / NAOT scenarios, a source generator (mirror of `JsonSourceGenerationContext`) can emit zero-reflection marshallers for declared types.

### 4.4 The module loader

```csharp
namespace Stash.Hosting.Modules;

public interface IStashModuleLoader
{
    bool TryLoad(string moduleName, out string source, out string? sourcePath);
}

public sealed class FileSystemModuleLoader : IStashModuleLoader { ... }
public sealed class EmbeddedResourceModuleLoader : IStashModuleLoader { ... }
public sealed class CompositeModuleLoader : IStashModuleLoader { ... } // tries each in order
public sealed class WatchingFileSystemModuleLoader : IStashModuleLoader { ... } // dev mode
```

Composable, replaceable, testable. The CLI uses `FileSystemModuleLoader`; the web app uses a composite of `EmbeddedResourceModuleLoader` (for built-in scripts) and `FileSystemModuleLoader` (for plugin directories).

### 4.5 What this is NOT

- **Not a new VM.** It wraps the existing one. All the runtime work stays in `Stash.Bytecode`.
- **Not a language change.** No new opcodes, no new AST nodes, no new keywords.
- **Not a replacement for the CLI.** The CLI keeps doing its REPL/shell-mode/build-mode jobs. It just uses `Stash.Hosting` internally for the script-runner path.
- **Not a sandbox/security boundary by itself.** Capabilities still gate stdlib; the Host SDK adds nothing security-wise beyond what `StashCapabilities` already provides.

---

## 5. Concurrency model — recommendation

The user explicitly asked for a recommendation. Here it is:

**Recommended: a small VM pool, owned by `IStashHost`, with checkout-per-call semantics.**

```
IStashHost (singleton)
  ├── VM pool (size = configurable, default = ProcessorCount × 2)
  │     ├── VM #1 ─ pre-warmed, stdlib registered, scripts pre-loaded
  │     ├── VM #2 ─ same
  │     └── ...
  └── For each CallAsync:
        1. Checkout a VM from the pool (ChannelReader, awaitable)
        2. Bind per-call state: Output writer, CancellationToken, StepCount=0
        3. Marshal args, look up function, call it
        4. Unmarshal result, build StashError list if failed
        5. Reset per-call state, return VM to pool
```

**Why pool, not VM-per-request:**

- VM construction is non-trivial: `StdlibDefinitions.CreateVMGlobals(caps)` builds a `Dictionary<string, StashValue>` with hundreds of entries (every stdlib function, every constant). Measurable on a per-request basis.
- Pre-compiled scripts live as pinned `Chunk` objects; pool VMs can be pre-bootstrapped (run the script's top-level once at startup) so per-call overhead is just `vm.Globals["fn"].Call(args)`.

**Why pool, not shared-with-locks:**

- A single shared VM serializes all requests; throughput becomes 1/avg-call-duration regardless of CPU count. For an admin dashboard maybe that's fine; for any meaningful traffic it's not.
- Shared-with-parallel-calls would require making the VM thread-safe — that's roughly a quarter of `Stash.Bytecode` rewritten with synchronization. Don't do this.

**Why pool, not VM-per-request:**

- Eliminates per-request bootstrap cost.
- Bounded memory: pool size caps the number of live VMs.
- Backpressure: if all VMs are busy, new requests await a slot (ChannelReader gives this for free) — preferable to the alternative (VM allocation under load → GC pressure → death spiral).

**The state-leak risk is real.** Stash scripts can mutate global state during a call. If `CallAsync` is the only entry point and scripts are well-behaved (mutate locals, not globals), pooling is safe. We add a debug-mode "global checksum" check that compares globals before and after each call and warns on drift. We document the contract: **functions called from the host must not mutate globals.**

**Variant for pure-function workloads:** offer a "single-call mode" where every `CallAsync` constructs a fresh VM. Slower but bullet-proof. Pick via `StashOptions.PoolingMode = { Pooled, FreshPerCall }`.

### Per-call state isolation

The pool only works if per-call state doesn't leak through the VM. That means:

- `Output` / `ErrorOutput` / `Input` need to become **per-call** parameters or `AsyncLocal<T>`-flowed values, not VM fields. The `IInterpreterContext` already exists; route I/O through it.
- `CancellationToken` needs to come in per call, not via constructor.
- `ScriptArgs` (currently a per-VM field) needs the same treatment.
- `StepCount` already resets — good.

This is small surgery on `VirtualMachine` (~200–400 SLOC change) but it's a precondition for the pool. Without it, two concurrent calls would fight over `vm.Output`.

---

## 6. Effort estimates (rough, not commitments)

| Component                                                  | Effort         | Risk   | Notes                                                        |
| ---------------------------------------------------------- | -------------- | ------ | ------------------------------------------------------------ |
| `Stash.Hosting` assembly skeleton + `IStashHost` interface | 1–2 days       | Low    | Mostly type definitions.                                     |
| `RunAsync` / `CallAsync` (sync wrapper)                    | 2–3 days       | Low    | Wraps existing API.                                          |
| Per-call state isolation in `VirtualMachine`               | 3–5 days       | Medium | Touching VM internals; need extensive test coverage.         |
| VM pool implementation                                     | 2–3 days       | Low    | Standard `Channel`-based pool.                               |
| Structured `StashError` mapping                            | 2 days         | Low    | Walk `RuntimeError.CallStack`, classify, build records.      |
| JSON ↔ StashValue bridge                                   | 2–3 days       | Low    | Recursive walker; well-trodden territory.                    |
| POCO marshaller (reflection-based)                         | 3–5 days       | Medium | Reflection-based version is easy; covers non-AOT hosts.      |
| POCO marshaller (source-generator)                         | 1–2 weeks      | High   | Source generator for AOT hosts; significant work. **Defer.** |
| `services.AddStash(...)` DI extension                      | 2–3 days       | Low    | Plumbing.                                                    |
| `IStashModuleLoader` + 3 default impls                     | 2–3 days       | Low    | Refactor of existing `ModuleLoader` delegate.                |
| `IHostedService` for startup compilation                   | 1–2 days       | Low    | Standard pattern.                                            |
| Migrate `Stash.Cli` to use `Stash.Hosting`                 | 3–5 days       | Medium | Big test surface; need to not regress CLI behavior.          |
| Documentation + samples (ASP.NET, console app, WPF)        | 3–5 days       | Low    |                                                              |
| **Subtotal (without source generator)**                    | **~4–6 weeks** |        |                                                              |
| **Subtotal (with source generator)**                       | **~6–8 weeks** |        |                                                              |

This is a real workstream, not a weekend project. But it's bounded, it's value-aligned with the "Stash is a serious .NET citizen" positioning, and it pays back every embedder forever.

---

## 7. What I'd build first (incremental rollout)

If we commit to this, I'd ship in three increments:

### Increment 1 — "Make the VM hostable" (1–2 weeks)

- Per-call state isolation in `VirtualMachine` (output, cancellation, args).
- Structured `StashError` extraction from `RuntimeError`.
- `StashEngine.CallAsync(name, args)` accepting `object?` args (using the JSON-shaped marshaller).
- JSON ↔ StashValue bridge.
- **Outcome:** The user's web API can do `await engine.CallAsync<ServerInfo>("create_server", req, ct)` reliably. No DI, no pool yet — host manages one engine per request.

### Increment 2 — "Make it fast and concurrent" (1–2 weeks)

- `IStashHost` + VM pool.
- `services.AddStash(...)` DI extension.
- Pre-compilation `IHostedService`.
- **Outcome:** Production-ready hosting story. ASP.NET handlers can fire concurrent requests without VM-per-request cost.

### Increment 3 — "Make it pleasant" (2–4 weeks)

- `IStashModuleLoader` + composable providers.
- POCO marshaller (reflection).
- Sample apps (ASP.NET, console, WPF).
- Docs.
- **Defer:** source-generator marshaller until someone asks for AOT hosting.

Each increment is independently shippable. You can stop after Increment 1 and have a real win for the use case.

---

## 8. Risks and tradeoffs

| Risk                                                              | Severity   | Mitigation                                                                                                              |
| ----------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------- |
| Per-call state isolation refactor breaks existing VM tests        | Medium     | Comprehensive test run after each change; feature-flag the new code path.                                               |
| Pool state-leak from misbehaving scripts                          | Medium     | Document contract; add debug-mode global drift detection; offer FreshPerCall mode.                                      |
| Marshaller boundary becomes a perf bottleneck                     | Low        | Target reflection-based first; profile; only build source-gen when needed.                                              |
| API surface bloats over time                                      | Medium     | Keep `Stash.Hosting` minimal; resist adding "convenience" methods that aren't used.                                     |
| Two ways to do everything (raw VM + Hosting SDK) confuse users    | Low-Medium | Document `Stash.Hosting` as "the way"; mark raw `VirtualMachine` use as advanced/internal.                              |
| Async wrappers leak threads on long-running scripts               | Low        | Use `Task.Run` with the VM's cancellation token; document that scripts should be CPU-bounded or yield via `time.sleep`. |
| AOT hosting (NAOT ASP.NET) doesn't work without source generators | Medium     | Document NAOT support level explicitly; reflection-based marshaller works for trimmed/JIT, not NAOT.                    |

---

## 9. What we are NOT solving

To be explicit about scope:

- **Not solving Stash language changes.** No new keywords, no new opcodes, no new AST. Pure host-side work.
- **Not solving real cooperative async.** The unfinished `await`/`async` infrastructure is a separate language design problem. The host SDK's "async" is just `Task`-shaped wrappers around synchronous VM execution.
- **Not solving the LSP/DAP NAOT problem.** Those depend on OmniSharp/DryIoc and would need their own analysis.
- **Not solving fine-grained sandboxing.** `StashCapabilities` stays as-is. Tenant-level / per-call capability scoping is a separate spec if/when needed.
- **Not solving cross-process embedding** (e.g., gRPC server hosting Stash). In-process only.
- **Not solving stash-as-a-NuGet-package** for non-.NET hosts. .NET hosts only.

---

## 10. Recommendation

**Pursue this, ambitiously.** The cost-benefit math here is the inverse of the IL compilation question:

- **Cost is bounded** (~4–6 weeks for a real Host SDK, with a clear MVP at 1–2 weeks).
- **Value is concrete and immediate** — the ASP.NET use case unblocks today; future embedders (Blazor apps, Worker Services, WPF tools) get the same benefits.
- **No language constraints sacrificed** — NAOT for the CLI stays, REPL stays, everything that works today keeps working.
- **No precedent risk** — `Stash.Hosting` is just a layer on top of public types we already maintain.
- **Strategic alignment** — Stash positioned as "embeddable .NET scripting language" is a clean and defensible niche. PowerShell hosting is famously awkward; Lua doesn't exist on .NET; IronPython is moribund. There's room for "the dynamic scripting language you can actually embed in an ASP.NET app."

**Start with Increment 1** (per-call state isolation + `CallAsync` + JSON bridge + structured errors). That alone solves the user's immediate use case. The pool and DI work follows naturally once the API shape is proven.

**Defer** the source-generator marshaller and any work targeting NAOT hosting until there's a concrete user request. Reflection-based marshalling covers Blazor, ASP.NET (JIT), Worker Services, and console apps — that's 95% of .NET hosting.

---

## 11. Decisions Recorded

> **Decision:** Treat the Stash embedding API as a first-class workstream worth dedicated investment.
> **Alternatives considered:** Status quo (let embedders reach into `VirtualMachine` directly); minimal `StashEngine` polish only; cross-process embedding via IPC.
> **Rationale:** The use case is concrete (ASP.NET-driven game-server admin tool) and representative of a broader class of .NET hosting scenarios. Current `StashEngine` covers the "run a script" case but not the "call a function with structured args from a web request" case, which is the dominant pattern for any real .NET host. The cost is bounded and the work doesn't compromise existing language properties (NAOT, REPL, deployment story).
> **Risks of reversal:** None significant — the `Stash.Hosting` assembly can be removed or replaced without affecting the language or the CLI.

> **Decision:** Concurrency model = pool of pre-warmed VMs, checkout-per-call, with mandatory per-call state isolation in `VirtualMachine`.
> **Alternatives considered:** VM-per-request (too much per-request overhead); shared VM with locks (kills throughput); shared VM with parallel calls (would require thread-safe VM, ~quarter of `Stash.Bytecode` rewritten).
> **Rationale:** Pool gives bounded memory, predictable backpressure, and amortizes VM bootstrap cost across many calls. Per-call state isolation is the precondition that makes pooling safe.
> **Risks of reversal:** Misbehaving scripts can leak global state across pool checkouts. Mitigated by documenting the contract and offering an opt-in `FreshPerCall` mode.

> **Decision:** Async surface = `Task`-shaped wrappers around synchronous VM execution; not a real cooperative async runtime.
> **Alternatives considered:** Building real cooperative async (yield to other requests during `time.sleep`/`http.get`); staying purely synchronous.
> **Rationale:** Real cooperative async requires completing the unfinished `await` opcode infrastructure plus reworking all blocking stdlib functions — large language-side investment. `Task.Run` wrappers solve the immediate "don't block thread-pool threads" problem at near-zero cost.
> **Risks of reversal:** If we later build real cooperative async, the wrapper-based methods become true non-blocking calls; signature stays compatible.

> **Decision:** Defer source-generator marshalling until there's concrete demand for NAOT-hosted Stash.
> **Alternatives considered:** Build source generator upfront; skip POCO marshalling entirely.
> **Rationale:** Reflection-based marshalling covers JIT-mode ASP.NET, Blazor Server, console apps, Worker Services — the vast majority of .NET hosting. NAOT-hosted ASP.NET is a small audience today. Source-generator complexity is significant and easy to add later without breaking the API.
> **Risks of reversal:** A future NAOT host hits a runtime "JsonSerializer requires reflection" wall; we add the source generator at that point.

---

## 12. Open questions (worth resolving before Increment 1 starts)

- **Naming:** `Stash.Hosting`, `Stash.Embedding`, or `Stash.Sdk`? My preference is `Stash.Hosting` — matches `Microsoft.Extensions.Hosting` convention, scans as ".NET-native."
- **Should `Stash.Cli` migrate to use `Stash.Hosting` immediately, or run side-by-side initially?** Migrating early forces the API to be honest; running side-by-side reduces risk. I lean toward "side-by-side until Increment 2 is stable, then migrate."
- **JSON serialization — `System.Text.Json` only, or also `Newtonsoft.Json` interop?** STJ-only is the modern answer; Newtonsoft interop is a one-class adapter if anyone asks.
- **Should the marshaller surface allow custom converters (à la `JsonConverter`)?** Probably yes for v1, but kept simple — `IStashConverter<T>` interface, register on options.
- **Telemetry: are we OK with introducing a dependency on `System.Diagnostics.DiagnosticSource`** (for `ActivitySource` / OpenTelemetry hooks)? Modern .NET answer is yes; it's in-box and zero-overhead when no listener is attached.
- **Should pre-compiled scripts be hot-reloadable in dev mode?** Likely yes, but a v2 concern. `WatchingFileSystemModuleLoader` would handle this.

---

## 13. VM thread-safety audit (added 2026-05-01)

§2 / §5 noted that `VirtualMachine` is not thread-safe but did not enumerate **what** is unsafe or **why each piece is unsafe**. This audit closes that gap. It is the evidence base for the decisions in §14.

The findings cluster into two distinct problems that must be solved separately:

- **Problem 1 — Per-VM mutable state** prevents one `VirtualMachine` from being called from two threads simultaneously. (Solved by §14 decision to never share a VM across threads — pool VMs and serialize within each.)
- **Problem 2 — Process-global mutable state** prevents two independent VM instances in the same process from being isolated. (Solved by §14 decisions on virtual cwd/env, multiplexed signals, per-VM IC slots, snapshot model, and stdlib static-state cleanup.)

### 13.1 Per-VM mutable state (Problem 1)

Every field below lives on a `VirtualMachine` instance and is read/written by opcodes during execution. Concurrent execution of two opcodes on the same VM corrupts at least one of these. Files cited use VS Code workspace-relative paths.

| Field                                                                                                          | File                                                                                       | Why unsafe under concurrent calls                                                                                                                                            |
| -------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `_stack[]`, `_sp`                                                                                              | [Stash.Bytecode/VM/VirtualMachine.cs](Stash.Bytecode/VM/VirtualMachine.cs)                 | Register file; every opcode reads/writes. Resized via `ArrayPool<StashValue>.Shared`. Two threads = data race on every instruction.                                          |
| `_frames[]`, `_frameCount`                                                                                     | same                                                                                       | Call frame stack — pushed/popped on Call/Return.                                                                                                                             |
| `_globals`, `_globalSlots[]`, `_constGlobals`, `_constGlobalSlots[]`, `_globalNameTable[]`                     | same                                                                                       | Global binding storage. Mutated by SetGlobal, `unset`, module loads.                                                                                                         |
| `_openUpvalues`                                                                                                | same                                                                                       | Closure capture list; scanned on every function return.                                                                                                                      |
| `_exceptionHandlers`                                                                                           | same                                                                                       | Try/catch handler stack; pushed on TryBegin, popped on TryEnd.                                                                                                               |
| `_importStack`                                                                                                 | same                                                                                       | Cycle detection set; mutated on every `import`.                                                                                                                              |
| `_context` (`VMContext`)                                                                                       | [Stash.Bytecode/Runtime/VMContext.cs](Stash.Bytecode/Runtime/VMContext.cs)                 | Holds DirStack, ActiveLocks, Output/Error/Input, LastError, ScriptArgs, TrackedProcesses, TrackedWatchers, LoggerState, ElevationActive, etc. — all mutated by stdlib calls. |
| `ReplGlobalAllocator`                                                                                          | same                                                                                       | Persistent across REPL inputs; itself non-thread-safe (see 13.2).                                                                                                            |
| `_debugCallStack`, `_debugThreadId`, `_lastDebugLinePerFrame[]`, `_loopCheckCounter`, `StepCount`, `StepLimit` | same                                                                                       | Debugger and step-budget state, mutated every instruction.                                                                                                                   |
| `_extensionRegistry`                                                                                           | [Stash.Bytecode/Runtime/ExtensionRegistry.cs](Stash.Bytecode/Runtime/ExtensionRegistry.cs) | Per-VM custom method registration.                                                                                                                                           |
| `_registeredTypeNames`, `_registeredTypeChecks`                                                                | [Stash.Bytecode/VM/VirtualMachine.cs](Stash.Bytecode/VM/VirtualMachine.cs)                 | C# ↔ Stash type bridges.                                                                                                                                                     |
| `LastExitCode`, `EmbeddedMode`, `_ct`, `_moduleLoader`, `_debugger`                                            | same                                                                                       | Configuration / cancellation / wiring fields.                                                                                                                                |

Already-thread-safe inside the VM:

- `ModuleCache`, `ModuleLocks` (`ConcurrentDictionary`).
- `Chunk` code, constants, source map, upvalue descriptors, local names — **immutable after compilation**.
- `StashValue` itself (struct of immutable primitives + reference handles).

### 13.2 Process-global mutable state (Problem 2)

The audit found **eleven** process-wide leak points. Each one means "two `VirtualMachine` instances in the same process are not isolated even if you never share a VM across threads."

| Leak                                                               | Location                                                                                                                                                                                                                                                                                           | Behaviour today                                                                                                             | Severity                                              |
| ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| Inline-cache writes on shared `Chunk`                              | [Stash.Bytecode/Bytecode/Chunk.cs](Stash.Bytecode/Bytecode/Chunk.cs) `ICSlots[]` + [Stash.Bytecode/Bytecode/ICSlot.cs](Stash.Bytecode/Bytecode/ICSlot.cs)                                                                                                                                          | Mutable struct array; concurrent VMs running same compiled chunk silently corrupt cached field offsets → wrong field reads. | **Critical** — silent miscompilation under load.      |
| `System.Environment.CurrentDirectory`                              | [Stash.Stdlib/BuiltIns/CurrentProcessImpl.cs](Stash.Stdlib/BuiltIns/CurrentProcessImpl.cs)                                                                                                                                                                                                         | `env.chdir` mutates real process cwd. VM2's `fs.read("./foo")` resolves against VM1's cwd.                                  | **High**                                              |
| `System.Environment.GetEnvironmentVariable/SetEnvironmentVariable` | [Stash.Stdlib/BuiltIns/EnvBuiltIns.cs](Stash.Stdlib/BuiltIns/EnvBuiltIns.cs)                                                                                                                                                                                                                       | Process-wide env vars.                                                                                                      | **High**                                              |
| Signal handler registry                                            | [Stash.Stdlib/BuiltIns/SignalImpl.cs](Stash.Stdlib/BuiltIns/SignalImpl.cs) `SignalHandlers` static dict                                                                                                                                                                                            | Last `signal.on(SIGTERM, …)` wins across VMs; earlier VMs' lock-cleanup defers never run on SIGTERM.                        | **High**                                              |
| `PromptBuiltIns` static delegate slots                             | [Stash.Stdlib/BuiltIns/PromptBuiltIns.cs](Stash.Stdlib/BuiltIns/PromptBuiltIns.cs) (`_promptFn`, `_continuationFn`, `_palette`, `_themes`, `_starters`, `_currentTheme`, `GitProbeHandler`, `ResetBootstrapHandler`, `ConventionFnResolver`, `ShellModeActive`, `_lineNumber`, `_renderingThread`) | Cross-VM bleed of REPL prompt state; irrelevant in embedded mode but corrupts CLI test isolation.                           | High in CLI tests; low in embedded scenarios.         |
| `ProcessBuiltIns` history hooks                                    | [Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs](Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs) `HistoryListProvider`, `HistoryClearHandler`, `HistoryAddHandler`                                                                                                                                             | Static delegate slots; only one provider registered at a time.                                                              | Medium                                                |
| `Console.Out` / `Console.Error` / `Console.In` defaults            | [Stash.Bytecode/Runtime/VMContext.cs](Stash.Bytecode/Runtime/VMContext.cs)                                                                                                                                                                                                                         | When `VMContext.Output` is left at its default, falls through to `Console.Out` → interleaved output across VMs.             | Medium (host can override).                           |
| `HttpClient` singleton                                             | [Stash.Stdlib/BuiltIns/HttpBuiltIns.cs](Stash.Stdlib/BuiltIns/HttpBuiltIns.cs) `_client`                                                                                                                                                                                                           | Thread-safe but shares cookies / connection pool across VMs.                                                                | Low                                                   |
| `GlobalSlotAllocator` (when shared)                                | [Stash.Bytecode/Compilation/GlobalSlotAllocator.cs](Stash.Bytecode/Compilation/GlobalSlotAllocator.cs)                                                                                                                                                                                             | `_nameToSlot`, `_nextSlot`, `_constValues` not synchronized; concurrent compilers sharing one race on slot allocation.      | Medium (only matters if compilation is parallelized). |
| POSIX signal registrations on `VMContext`                          | [Stash.Bytecode/Runtime/VMContext.cs](Stash.Bytecode/Runtime/VMContext.cs) `_sigtermReg`, `_sighupReg`                                                                                                                                                                                             | Each VMContext registers SIGTERM/SIGHUP cleanup handlers. Last registration wins; earlier VMs' lock release never runs.     | High when multi-VM.                                   |
| `Random.Shared`                                                    | [Stash.Stdlib/BuiltIns/MathBuiltIns.cs](Stash.Stdlib/BuiltIns/MathBuiltIns.cs)                                                                                                                                                                                                                     | ThreadLocal — safe across threads but not per-VM (two VMs on same thread share).                                            | Low                                                   |

### 13.3 Stash value-type thread safety (orthogonal concern)

Even if the VM is fully isolated, **`StashDictionary`, `StashArray`, and `StashStruct` instances themselves are not thread-safe** ([Stash.Core/Runtime/Types/](Stash.Core/Runtime/Types/)). If a script captures one in a closure that escapes to multiple threads (e.g. via `signal.on` callbacks invoked from multiple VMs, or via shared frozen modules), concurrent mutations corrupt internal storage. The §14 decisions do not solve this directly; the snapshot model (§14.5) sidesteps it by making shared state immutable (`const` only).

---

## 14. Design decisions (committed) and updated implementation plan

These supersede §12 ("Open questions"). They were resolved through interactive design review on 2026-05-01.

### 14.1 Concurrency model — pool of independent VMs

> **Decision:** Each request/work-item runs on its own `VirtualMachine` instance, rented from a pool managed by `IStashHost`. Within a VM, execution remains single-threaded. Concurrency comes from running _different_ VMs on different threads.
>
> **Alternatives considered:**
>
> - Single shared VM made re-entrant across threads: would require atomicising every field in §13.1 — `_stack`, `_frames`, `_globals`, `_openUpvalues`, `_exceptionHandlers`, defer stacks, IC slots — and reworking defer/lock/try-catch semantics to be thread-local. Massive surface, hurts single-threaded perf, and Stash's runtime model (defer LIFO per frame, file locks per call stack, `try/catch` unwinding a single frame chain) is fundamentally a single-threaded call-stack model.
> - Hybrid VM-per-scope (e.g. per tenant) with a queue: solvable from the pool model later as a layered policy; not foundational.
>
> **Rationale:** Stash's defer/lock/exception/upvalue semantics are inherently call-stack-local. Trying to make one stack serve two threads forces those semantics to be redefined for the multi-thread case, which is a language change, not just an implementation change. The pool model preserves all single-threaded semantics exactly and pushes parallelism to a layer (`IStashHost`) where it's an orthogonal concern.
>
> **Risks of reversal:** None at the language level — pool model is purely additive. A future "true shared VM" could be added without removing the pool. The pool itself is straightforward to remove if it proves wrong.

### 14.2 Bytecode sharing — per-VM IC slot clone on chunk load

> **Decision:** `Chunk` becomes shareable across VMs by cloning its `ICSlots[]` array per-VM at load time. The rest of the chunk (code, constants, source map, upvalue descriptors, local names) stays shared and immutable. The clone is a single `Array.Copy` of a fixed-size struct array — measured cost is negligible compared to the bootstrap cost it amortises.
>
> **Alternatives considered:**
>
> - Move IC slots into the VM (`vm._icSlotsByChunkId[…]`): cleaner separation but adds an indirection on every cached field access, and complicates lookup (need a chunk→id map). Possible v2 if the per-VM clone becomes a memory pain.
> - Recompile per VM, don't share: defeats the perf goal — for an ASP.NET endpoint hit 1000×/s, compiling 1000× wastes both CPU and the IC warming benefit.
> - Atomic IC writes via `Interlocked`: IC slots store multi-field guards (struct shape pointer + offset + state); torn writes between fields would be incorrect, not just slow. Lock-free designs here are very tricky to get right.
>
> **Rationale:** Per-VM clone is the smallest change with the strongest correctness guarantee. The IC array is small (one slot per `GetField` site in the chunk) and the clone happens once per chunk load, not per call. Binary serialization format (`.stashc`) is unaffected — IC slots are runtime-only and don't appear in the on-disk format.
>
> **Risks of reversal:** If memory cost becomes a concern for hosts that load many large chunks, migrate to the in-VM-array design (Option A in the discussion). Public API doesn't need to change.

### 14.3 Process-global resources — virtual cwd/env, multiplexed signals

> **Decision:**
>
> - **`env.chdir` / `env.popDir` / `env.dirStack`** stop mutating `System.Environment.CurrentDirectory`. They mutate a new `VMContext.WorkingDirectory` field instead. **All stdlib I/O** (`fs.*`, `path.resolve`, `process.spawn`, `io.*`, `tpl.renderFile`, glob expansion, etc.) routes relative-path resolution through `VMContext.WorkingDirectory`, falling back to `Environment.CurrentDirectory` only at VM construction time as the initial value.
> - **`env.set` / `env.unset`** stop mutating `System.Environment`. They mutate a new `VMContext.EnvVars` overlay (`Dictionary<string, string?>`, with `null` meaning "unset"). `env.get` consults the overlay first, then falls back to `System.Environment.GetEnvironmentVariable`.
> - **`process.spawn`** explicitly sets `ProcessStartInfo.WorkingDirectory = ctx.WorkingDirectory` and merges `ctx.EnvVars` into `ProcessStartInfo.EnvironmentVariables`. Spawned processes therefore inherit the **VM's** cwd/env, not the **process's** cwd/env. This is the behaviour scripts intuitively expect.
> - **Signals (`signal.on` / `signal.off`)** become **multiplexed**: `SignalImpl.SignalHandlers` becomes `ConcurrentDictionary<string, List<(IInterpreterContext, IStashCallable)>>`. When the OS signal fires, every registered handler is invoked in registration order, each wrapped in `try/catch` so one VM's crash doesn't prevent another's defer/lock cleanup from running. `signal.off(name, handler)` removes a specific handler; `signal.off(name)` removes all handlers registered by the calling VM (identified by `IInterpreterContext` reference).
>
> **Alternatives considered:**
>
> - **Real cwd/env (current):** keeps stdlib trivially correct for CLI but unusable in any multi-VM scenario.
> - **Signals last-writer-wins (current):** matches single-VM semantics but means SIGTERM in a multi-VM process only runs one VM's cleanup → unreleased file locks.
> - **Signals first-writer-wins with error:** forces hosts to coordinate signal registration, but in a pooled VM scenario that's impossible (every pool VM would need to skip registration — fragile).
>
> **Rationale:** Virtual cwd/env is the only design that makes per-VM isolation real while still giving spawned processes a sensible inherited environment. The "spawned processes inherit the VM's view" semantic is what scripts already expect today (when they call `env.chdir` then `process.spawn`, they expect the spawn to see the new cwd) — the change is just that the inheritance now goes through `ProcessStartInfo` instead of through the process-wide `System.Environment`. Multiplexed signals is the only design that lets SIGTERM correctly trigger every active VM's defer/lock cleanup.
>
> **Affected built-ins (non-exhaustive, for the implementation checklist):**
>
> - `env.chdir`, `env.popDir`, `env.dirStack`, `env.dirStackDepth`, `env.withDir`
> - `env.get`, `env.set`, `env.unset`
> - `fs.read`, `fs.write`, `fs.exists`, `fs.list`, `fs.glob`, `fs.realpath`, `fs.mkdir`, `fs.copy`, `fs.move`, `fs.remove`, `fs.stat`, `fs.touch`, etc.
> - `path.resolve`, `path.absolute`
> - `io.open` and friends
> - `process.spawn`, `process.run`, `$(...)` shell expansion (via `ShellRunner`)
> - `tpl.renderFile` and any other path-accepting stdlib functions
> - `signal.on`, `signal.off`
>
> **Risks of reversal:** Behaviour change for scripts that today rely on `env.chdir` mutating the real process cwd (e.g. as a way to affect a subsequent C# host call). Mitigation: hosts that genuinely want the old behaviour can read `vm.Context.WorkingDirectory` from C# and apply it themselves; or we can ship a `StashHostOptions.UseRealCurrentDirectory = true` opt-in for migration. Default is virtual.

### 14.4 Stdlib scope — full surface, hosts opt out via capabilities

> **Decision:** All 35 stdlib namespaces remain available to embedded hosts by default. Hosts choose what to enable via the existing `StashCapabilities` bitmask. There is **no** "embedded profile" or "sandbox profile" preset that strips functions out of the language.
>
> **Alternatives considered:**
>
> - Three preset profiles (`Full`, `Embedded`, `Sandbox`): nice DX one-liner but presumes we know what hosts want — we don't. The driving use case (game-server manager) needs `process.spawn`, `fs.write`, `http.get`, `signal.on`, file uploads, systemd integration, the works.
> - Capability-only enumeration with no defaults: tedious for the common case.
>
> **Rationale:** Restricting what a script can do is a host-level security/policy decision, not a language-level one. The capability bitmask already exists and is sufficient. Building "presets" we'd then have to argue about every time someone's use case doesn't fit creates friction without adding safety.
>
> **Risks of reversal:** None; presets can always be added later as a thin layer over capabilities.

### 14.5 Snapshot model — `const` is frozen+shared, `let` is deep-cloned per call

> **Decision:** When `IStashHost.CompileAsync` returns a `CompiledScript`, we execute its top level **once** to produce a "template" globals state. Subsequent `CallAsync`/`InvokeAsync` invocations rent a VM, restore globals from the template, run the requested function, and return the VM to the pool with no globals mutation persisted.
>
> Restore semantics depend on how the global was declared in the script:
>
> - **`const X = …`** values are **frozen and shared by reference** across all pool VMs. Reference-typed values (`StashDictionary`, `StashArray`, `StashStruct`) get their existing immutability marker set at template-finalisation time so any attempt to mutate them at runtime throws `ValueError: cannot modify const value` — exactly as today's const-reassignment error works for direct assignment, extended to nested mutations.
> - **`let y = …`** values are **deep-cloned** from the template into the rented VM's globals on each call. Each call sees a fresh copy.
>
> **Alternatives considered:**
>
> - Deep-clone everything (Option A): predictable but pays the deep-clone cost even for genuinely-immutable config dicts that would benefit from sharing.
> - Freeze everything (Option B): forces all top-level state to be immutable, breaking scripts that today have `let cache = dict.new()` at module scope and mutate it in handlers. Surprising for script authors.
> - Copy-on-write reference values (Option C): cleanest UX but requires a "maybe-shared" bit on every collection plus a write-barrier on every mutation opcode. Significant VM complexity for marginal benefit.
> - No automatic snapshot (Option D): forces hosts to call `script.SetGlobal` for each per-request value. Cleanest contract but most restrictive — and doesn't help scripts that genuinely need per-request scratch state computed inside Stash.
>
> **Rationale:** Option E uses a language feature script authors already understand — `const` vs `let` — to express the snapshot contract. No new keyword, no new mental model. The perf characteristic is intuitive ("mark expensive things `const` to amortise them") and the safety characteristic is intuitive ("`const` shared values can't be mutated, mutations to `let` values are call-local"). The freeze-on-finalise extends today's `const` write-protection to nested mutations, which is arguably a bug fix anyway: `const D = {a: 1}; D.a = 2;` arguably should not silently succeed today.
>
> **Implementation notes:**
>
> - Deep clone covers `StashDictionary`, `StashArray`, `StashStruct` (recursive). Primitives are value types — no clone needed. Closures (`StashClosure`) are immutable references — shared, like `const`. `StashError` instances should be deep-cloned (they carry mutable `properties` dicts).
> - Cycles in the template state are an edge case. For v1, detect-and-fail is acceptable: deep-clone with a visited set, throw `RuntimeError: cyclic top-level state cannot be snapshotted` if a cycle is found. v2 can switch to cycle-preserving clone if anyone hits this in practice.
> - Module imports: top-level state inside imported modules follows the same `const`/`let` rule, evaluated once at template build time. Module functions and `const` exports are shared across pool VMs. (This aligns with §14.6.)
>
> **Risks of reversal:** Scripts that today mutate top-level `let` values across REPL inputs and rely on persistence will see those mutations reset between embedded host calls. This is the desired semantic for embedded use but surprising for REPL semantics — which is fine because REPL doesn't go through the snapshot path. The CLI's run-a-file mode also doesn't go through snapshot (single execution, no pool).

### 14.6 Module sharing — frozen shared module cache at the host level

> **Decision:** `IStashHost` owns a single shared **module cache** mapping `(moduleName) → frozen module exports`. The first VM to import a module compiles it, runs its top level, snapshots the result (per §14.5 rules — `const`/functions shared, `let` deep-cloned per importing VM), and stores the snapshot. Subsequent imports by other pool VMs reuse the snapshot.
>
> **Rationale:** Modules in Stash are typically pure metadata (function definitions, constants, struct/enum types). Re-running every `import @stash/json` per pool VM is pure waste. Sharing the snapshot cuts per-VM bootstrap cost dramatically. Module authors who genuinely need per-VM mutable state can expose a factory function (`fn createState() { … }`) and let scripts call it; this is a documented best practice, not a language change.
>
> **Risks of reversal:** A module that mutates its own top-level `let` state during imports for caching purposes will see that cache shared across all importing VMs (same as `const`). For most modules this is fine or even desired. For modules that genuinely need per-import mutable state, the factory-function pattern is the workaround. This matches how Python, Node, and most other module systems behave.

### 14.7 Marshalling and errors

> **Decision:**
>
> - **Argument marshalling is liberal:** `IStashHost.CallAsync(name, args)` accepts `object?`. Auto-conversion covers C# primitives (`int`, `long`, `double`, `bool`, `string`, `null`), `IDictionary<string, object?>` → `StashDictionary`, `IEnumerable<object?>` → `StashArray`, anonymous types → `StashDictionary` (property name → value). Anything else throws `ArgumentException` with a clear "no marshaller registered for type X" message. POCO/record reflection-based marshalling is a v2 nice-to-have, kept out of v1 to keep the AOT story clean.
> - **Return marshalling is generic:** `CallAsync<T>(...)` auto-converts the returned `StashValue` to `T` using the inverse rules. `T = StashValue` short-circuits (no conversion). `T = JsonElement` triggers the JSON bridge.
> - **Errors propagate via both modes, host opts in:** `CallAsync<T>` throws `StashScriptException` (wrapping `StashError` and the formatted stack trace) on failure. `TryCallAsync<T>` returns `StashResult<T>` (the existing struct from §4.1 — `Success`, `Value`, `Errors`, `Duration`, `StepsExecuted`) without throwing. Hosts choose per call.
>
> **Rationale:** Liberal marshalling matches `System.Text.Json` ergonomics and is what ASP.NET developers expect. Throw-vs-Result both have legitimate use cases (controllers prefer throw + middleware; background workers prefer Result + explicit handling). Reflection-POCO is the right deferred pick — it would force a decision about source generators for AOT that we don't need to make today.
>
> **Risks of reversal:** Adding POCO marshalling later is purely additive. Source-generator marshalling can be layered on without changing the API.

### 14.8 Limits and cancellation — per-script defaults + per-call overrides + opt-in untrusted profile

> **Decision:**
>
> - **Per-script (set on `CompiledScript` via `ScriptOptions`):** `MaxSteps` (default unlimited), `MaxCallDepth` (default 1000), `MaxOpenLocks` (default 32).
> - **Per-call (passed to `InvokeAsync`):** `CancellationToken ct`, optional `TimeSpan timeout` (wraps a `CancellationTokenSource(timeout)`), optional override of `MaxSteps`.
> - **Behaviour:** Step limit → `StepLimitExceededException` (already exists). Cancellation/timeout → `OperationCanceledException` raised after pending defers execute. Call depth → `RuntimeError("maximum call depth exceeded")`. Lock limit → `IOError("maximum open locks exceeded")`.
> - **Untrusted profile (opt-in flag):** `StashScriptOptions.Untrusted = true` flips defaults to conservative values (`MaxSteps = 1_000_000`, `MaxCallDepth = 200`, `MaxOpenLocks = 4`). Hosts that want true sandbox-grade restrictions still need to drop capabilities (e.g. clear `StashCapabilities.Process`, `StashCapabilities.Network`) — `Untrusted` only sets resource limits, it doesn't auto-disable capabilities. Memory budget and `ScratchDirectory` jail are explicitly **not** in v1 (memory is unenforceable in pure managed code; jail is too policy-specific).
>
> **Rationale:** Defaults are wide-open per §14.4. The untrusted flag is opt-in convenience for security-conscious hosts who want a sane starting point without enumerating every limit. Memory enforcement requires per-allocation tracking that we'd need to add to every `StashDictionary.Add`, every `StashArray.Push`, every string concatenation — that's a separate workstream. Path jailing is something hosts can implement themselves by injecting a custom `IStdlibProvider` that wraps `fs.*`.
>
> **Risks of reversal:** None — these are additive convenience knobs.

### 14.9 CLI relationship — SDK becomes the primitive layer; CLI rebuilds on top

> **Decision (long-term target):** `Stash.Hosting` becomes the canonical embedding API. `Stash.Cli` is rebuilt to use `Stash.Hosting` for its script-execution path (REPL, `stash run`, `stash exec`). REPL-specific concerns (multi-line reader, prompt rendering, shell mode, debug integration) move to a `Stash.Cli.Internal` package or stay in `Stash.Cli` itself, layered above the host SDK.
>
> **Decision (v1 rollout):** Ship `Stash.Hosting` standalone alongside the unchanged CLI. After the SDK API has stabilised across one or two real embedding consumers, migrate the CLI as a follow-up workstream.
>
> **Rationale:** Eating our own dog food long-term ensures the SDK stays honest — every CLI feature has to be expressible through it. But forcing the CLI migration as a precondition for shipping the SDK turns a 4–6-week project into a 3-month project and pushes back the embedding use case unnecessarily. Two-stage rollout gives us velocity now and convergence later.
>
> **Risks of reversal:** Maintaining two code paths (CLI direct VM access + SDK facade) until convergence creates duplication. Mitigated by treating `Stash.Cli`'s direct `VirtualMachine` access as deprecated-internal during the gap, and making `Stash.Hosting` strictly more capable than the direct access from day one.

### 14.10 Naming and packaging

> **Decision:** Package name is `Stash.Hosting`. ASP.NET-specific DI extensions live in a separate `Stash.Hosting.AspNetCore` package that depends on `Stash.Hosting` and `Microsoft.Extensions.DependencyInjection.Abstractions`. This keeps `Stash.Hosting` free of ASP.NET dependencies so it works in console apps, Worker Services, WPF tools, Blazor Server, etc.
>
> **Rationale:** Matches the `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Hosting.AspNetCore` convention. Hosts that don't want DI don't pay for the dependency. The previously-considered names (`Stash.Embedding`, `Stash.Sdk`) are less recognisable to .NET developers.

---

## 15. Updated implementation plan (supersedes §7)

The §7 three-increment plan still holds at a high level, but the §14 decisions add scope to each increment. Updated breakdown:

### Increment 1 — "Make the VM hostable" (~2 weeks)

Foundation changes that any embedding scenario needs.

1. **Per-VM IC slot clone** ([Stash.Bytecode/Bytecode/Chunk.cs](Stash.Bytecode/Bytecode/Chunk.cs)) — new `Chunk.CloneIcSlots()` returning a fresh `ICSlot[]`; `VirtualMachine.LoadChunk` (or equivalent entry) clones on first use.
2. **Virtual cwd/env on `VMContext`** ([Stash.Bytecode/Runtime/VMContext.cs](Stash.Bytecode/Runtime/VMContext.cs)) — add `WorkingDirectory` (string), `EnvVars` (Dictionary<string,string?>); migrate `env.*`, `fs.*`, `path.resolve`, `process.spawn`, `tpl.renderFile`, glob expansion, `io.open` to consult them. Add `StashHostOptions.UseRealCurrentDirectory` opt-in for migration.
3. **Multiplexed signals** ([Stash.Stdlib/BuiltIns/SignalImpl.cs](Stash.Stdlib/BuiltIns/SignalImpl.cs)) — `SignalHandlers` becomes `ConcurrentDictionary<string, List<(IInterpreterContext, IStashCallable)>>`; OS handler iterates list with `try/catch` per entry; `signal.off(name)` removes the calling context's entries.
4. **Per-call state isolation in `VirtualMachine`** — `Output`/`Error`/`Input`, `ScriptArgs`, `_ct` move out of constructor and into `ExecuteContext`-style per-call parameters. `StepCount`/`StepLimit` stay per-VM but reset on each `CallAsync`.
5. **Static state cleanup hooks** — `StashHost` calls `PromptBuiltIns.Reset()`, `ProcessBuiltIns.ResetHistoryHooks()` on disposal so test isolation works.
6. **Structured `StashError` extraction** — walk `RuntimeError.CallStack`, classify by `errorType`, build `StashError` records carrying the `SourceSpan` and the formatted stack frames.
7. **JSON ↔ StashValue bridge** — recursive walker in both directions; `StashValue.FromJsonElement(JsonElement)`, `StashValue.WriteJson(Utf8JsonWriter)`, `StashValue.ToJsonString()`.
8. **`StashHost` facade + `CompiledScript` + `CallAsync<T>` / `TryCallAsync<T>`** — single-VM mode (no pool yet); host tells the VM "execute this function with these args, return the result."

**Outcome:** A single-VM embedding works correctly. ASP.NET hosts that allocate one engine per request get correct, isolated behaviour and structured errors. No pool or DI yet.

### Increment 2 — "Make it fast and concurrent" (~2 weeks)

Pooling, snapshot model, and DI plumbing.

1. **Snapshot model implementation** — top-level execution captures a "template" globals state; `CallAsync` clones `let` values per-VM and shares `const` values by reference; `const` reference values get the immutable flag set so nested mutation throws.
2. **Deep-clone primitives for `StashDictionary` / `StashArray` / `StashStruct` / `StashError`** — recursive; cycle detection with throw on cycle for v1.
3. **`IStashHost` VM pool** — `Channel`-based pool, configurable size (default `ProcessorCount * 2`), checkout-per-call with backpressure when exhausted.
4. **Shared frozen module cache on `IStashHost`** — first import compiles+runs+snapshots; subsequent imports reuse.
5. **Per-script and per-call limits** — `MaxSteps`, `MaxCallDepth`, `MaxOpenLocks` enforcement; cancellation token wired through to `_ct` per call.
6. **Untrusted profile flag** — flips defaults; documented in `Stash.Hosting` README.
7. **`Stash.Hosting.AspNetCore`** — `services.AddStash(...)`, `IHostedService` for startup pre-compilation, `StashHealthCheck`.

**Outcome:** Production-ready hosting story for ASP.NET. Multiple concurrent requests run on independent pooled VMs with snapshot-restored globals. Module imports are amortised across the pool.

### Increment 3 — "Make it pleasant" (~2–4 weeks)

DX polish.

1. **`IStashModuleLoader` + composable providers** (`FileSystemModuleLoader`, `EmbeddedResourceModuleLoader`, `CompositeModuleLoader`, optional `WatchingFileSystemModuleLoader`).
2. **Reflection-based POCO marshaller** — handles records, anonymous types, plain classes; `JsonSerializerOptions`-style configuration.
3. **Telemetry hooks** — `ActivitySource`-based spans per `CallAsync`; metrics (`Histogram<double>` for call duration, `Counter<long>` for errors).
4. **Sample apps** — ASP.NET Core API, console runner, Worker Service.
5. **Documentation** — new `docs/Embedding — Hosting Stash in .NET.md`.
6. **CLI migration scoping** — write a separate spec for `Stash.Cli` rebuild on `Stash.Hosting`; do not implement in this workstream.

**Deferred to v2 (tracked separately):**

- Source-generator marshaller for NAOT-hosted ASP.NET.
- Memory budget enforcement (`MaxMemoryBytes`).
- Path-jail (`ScratchDirectory`) as part of an extended untrusted profile.
- Copy-on-write `let`-snapshot for hot scripts.
- Cycle-preserving deep clone in snapshots.
- Cooperative async (real `await` runtime).
- Cross-process embedding (gRPC host).

---

## 16. Open work to spec separately

This document is sufficient to begin Increment 1. Two follow-up specs are needed before later increments start:

1. **CLI rebuild on `Stash.Hosting`** — scoping document for §14.9 long-term target. Should enumerate every CLI feature (REPL, shell mode, debug, `stash run`, `stash exec`, `stash check`, `stash format`, `stash test`) and map each to either "uses `Stash.Hosting` directly" or "stays in `Stash.Cli` because it's REPL/shell/debug-specific."
2. **Capability granularity v2** (Gap 8) — only if a host actually needs sub-namespace gating (e.g. allow `fs.read` but not `fs.write`). Not blocking for the game-server use case.

---

## 17. Summary

The audit confirms the §2 / §5 intuition: the VM is single-threaded by design, and trying to make one instance re-entrant across threads is the wrong abstraction layer. The right answer is **isolated per-VM state + a host-managed pool**. Achieving real isolation requires the eleven process-global leaks in §13.2 to be addressed; §14 decisions cover all of them with focused, low-risk changes.

The §14.5 snapshot model (using `const`/`let` semantics to express the share-vs-clone contract) is the single most important design decision, because it gives the per-request startup cost the right shape: _O(let-state-size)_ instead of _O(everything)_ or _O(0 + footguns)_. It's also the most novel — worth particular attention from reviewers and the implementer.

The work fits cleanly into the §7 three-increment rollout, and Increment 1 alone is enough to unblock the driving use case.
