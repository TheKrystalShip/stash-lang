# Networking — First-Class IP Addresses & Net Namespace

> **Status:** Proposal
> **Created:** April 2026
> **Purpose:** Make networking feel as natural as command literals in Stash. Introduce IP addresses as a language-level type with literal syntax, operator integration, and a comprehensive `net` namespace for DNS, connectivity, TCP/UDP, and interface discovery.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [IP Address Literals](#2-ip-address-literals)
3. [Operator Integration](#3-operator-integration)
4. [The `net` Namespace](#4-the-net-namespace)
5. [Full Example — Network Health Check](#5-full-example--network-health-check)
6. [Implementation Phases](#6-implementation-phases)
7. [Design Decisions](#7-design-decisions)

---

## 1. Motivation

Stash treats shell commands as first-class with `$(...)` — networking deserves the same treatment. System administration is inherently network-heavy: checking connectivity, scanning subnets, verifying firewall rules, resolving DNS, managing services across hosts. Yet Stash today only offers `http`, `ssh`, and `sftp` — no way to represent IP addresses, no DNS resolution, no port checking, no raw TCP/UDP.

A sysadmin scripting language that can't natively express `192.168.1.0/24` or check if a host is in a subnet is like a shell that can't pipe commands. This proposal fills that gap with three layers:

1. **IP address literals** — a new language-level type with literal syntax
2. **Operator integration** — bitwise, comparison, `in`, and arithmetic work on IP addresses
3. **A `net` namespace** — DNS, ping, port checking, TCP/UDP, interface discovery

---

## 2. IP Address Literals

### Syntax

IP address literals use the `@` prefix — a single character followed by the address with no quotes:

```stash
let addr   = @192.168.1.1             // IPv4
let v6     = @::1                      // IPv6 loopback
let mapped = @::ffff:192.168.1.1      // IPv4-mapped IPv6
let cidr   = @10.0.0.0/24             // Subnet (CIDR notation)
let any    = @0.0.0.0                  // Wildcard
```

The `@` sigil is natural for addresses — it already means "at" in SSH (`user@host`), email (`user@domain`), and network contexts. The lexer sees `@` and enters IP-address scanning mode, consuming everything until whitespace, an operator, or a delimiter.

### Why Not Bare `192.168.1.1`?

Bare IP addresses create deep lexer ambiguity. `10.0.0.1` starts with `10.0` which is a valid float. `192.168` would tokenize as `192`, `.`, `168` (integer-dot-integer). The two-pointer scanner would need 4-dot lookahead and still couldn't distinguish `10.0` (float) from the start of `10.0.0.1` (IP) without backtracking.

IPv6 makes bare literals completely impossible — `::1` and `fe80::1%eth0` can't be expressed without some form of delimiter.

### Runtime Type: `StashIpAddress`

A new class in `Stash.Core/Runtime/Types/`, following the `StashRange` pattern:

```csharp
public class StashIpAddress
{
    public byte[] Bytes { get; }         // 4 bytes (v4) or 16 bytes (v6)
    public int Version { get; }          // 4 or 6
    public int? PrefixLength { get; }    // CIDR prefix (null = host address)

    public bool Contains(StashIpAddress other) { ... }  // CIDR containment
    public bool IsLoopback { get; }
    public bool IsPrivate { get; }
    public bool IsLinkLocal { get; }

    public override string ToString() => ...;  // "192.168.1.1" or "10.0.0.0/24"
}
```

### Type System Integration

```stash
let addr = @192.168.1.1
io.println(addr is ip)          // true
io.println(typeof(addr))        // "ip"
io.println($"Server: {addr}")   // "Server: 192.168.1.1"
```

---

## 3. Operator Integration

This is where the recently-implemented bitwise operators become transformative when applied to IP addresses.

### 3.1 Bitwise Operators — Subnet Masking

```stash
let addr      = @192.168.1.100
let mask      = @255.255.255.0
let network   = addr & mask            // @192.168.1.0
let broadcast = network | ~mask        // @192.168.1.255
let inverted  = ~mask                  // @0.0.0.255 (wildcard mask)
```

This is exactly what sysadmins write constantly with `ipcalc` or Python's `ipaddress` module — except here it's native syntax with operators they already know.

### 3.2 Comparison Operators

```stash
let a = @10.0.0.1
let b = @10.0.0.254
io.println(a < b)                       // true (lexicographic byte comparison)
io.println(a == @10.0.0.1)        // true (value equality)
io.println(a != b)                      // true
```

### 3.3 The `in` Operator — CIDR Containment

```stash
let subnet = @192.168.1.0/24

io.println(@192.168.1.50  in subnet)   // true
io.println(@192.168.2.1   in subnet)   // false
io.println(@10.0.0.1      in subnet)   // false
```

This mirrors how `5 in 1..10` works for ranges — subnets _are_ ranges of IP addresses. The `in` operator with CIDR notation is arguably the single most useful feature for firewall rules, ACLs, and network segmentation scripts.

### 3.4 Arithmetic — Address Offset

```stash
let base = @10.0.0.0
let host = base + 42               // @10.0.0.42
let next = @192.168.1.254 + 1   // @192.168.2.0 (wraps octets correctly)

for (let i in 1..255) {
    let host = @192.168.1.0 + i
    if (net.ping(host).alive) {
        io.println($"  {host} is up")
    }
}
```

---

## 4. The `net` Namespace

Stash already has `http`, `ssh`, and `sftp`. The `net` namespace fills the gap for everything else.

### 4.1 DNS Resolution

```stash
let addr = net.resolve("example.com")          // @93.184.216.34
let all  = net.resolveAll("example.com")       // [@93.184.216.34, ...]
let name = net.reverseLookup(@8.8.8.8)  // "dns.google"
let mx   = net.resolveMx("example.com")       // [{host: "mail.example.com", priority: 10}, ...]
let txt  = net.resolveTxt("example.com")       // ["v=spf1 ...", ...]
```

### 4.2 Connectivity Testing

```stash
// Ping with clean struct return
let result = net.ping(@8.8.8.8)
io.println(result.alive)       // true
io.println(result.latency)     // 12 (ms)
io.println(result.ttl)         // 117

// Batch ping — scan a subnet
let alive = net.pingAll(@192.168.1.0/24, { timeout: 500 })
for (let host in alive) {
    io.println($"  {host.ip} — {host.latency}ms")
}

// Port checking
let open = net.isPortOpen(@192.168.1.1, 22)    // true/false
let open = net.isPortOpen("example.com", 443)          // also accepts hostnames

// Scan port range
let ports = net.scanPorts(@192.168.1.1, 1..1024, { timeout: 200 })
for (let p in ports) {
    io.println($"  :{p.port} ({p.protocol}) — {p.state}")
}
```

### 4.3 TCP Client

```stash
let conn = net.tcpConnect(@192.168.1.1, 6379)   // Redis, raw TCP
net.send(conn, "PING\r\n")
let response = net.recv(conn)                           // "+PONG\r\n"
net.close(conn)

// With timeout and TLS
let tls = net.tcpConnect("example.com", 443, { tls: true, timeout: 5000 })
net.send(tls, "GET / HTTP/1.0\r\nHost: example.com\r\n\r\n")
let data = net.recv(tls)
net.close(tls)
```

### 4.4 UDP

```stash
// Send syslog message
net.udpSend(@10.0.0.1, 514, "<14>Test message from Stash")

// Listen for UDP packets
let sock = net.udpListen(5000)
let packet = net.recv(sock, { timeout: 3000 })
io.println($"From {packet.sender}: {packet.data}")
net.close(sock)
```

### 4.5 Network Interfaces

```stash
let interfaces = net.interfaces()
for (let iface in interfaces) {
    io.println($"  {iface.name}: {iface.ip} ({iface.mac}) up={iface.up}")
}
// Output: eth0: 192.168.1.50 (aa:bb:cc:dd:ee:ff) up=true

// Get specific interface
let eth0 = net.interface("eth0")
io.println(eth0.ip)        // @192.168.1.50
io.println(eth0.gateway)   // @192.168.1.1
io.println(eth0.subnet)    // @192.168.1.0/24
```

### 4.6 Subnet Calculation

```stash
let info = net.subnetInfo(@10.20.30.99/24)
io.println(info.network)     // @10.20.30.0
io.println(info.broadcast)   // @10.20.30.255
io.println(info.mask)        // @255.255.255.0
io.println(info.wildcard)    // @0.0.0.255
io.println(info.hostCount)   // 254
io.println(info.firstHost)   // @10.20.30.1
io.println(info.lastHost)    // @10.20.30.254
```

---

## 5. Full Example — Network Health Check

```stash
#!/usr/bin/env stash

// Network health check for data center subnet

struct HostStatus {
    ip,
    alive,
    latency,
    sshOpen,
    httpOpen
}

let subnet = @10.0.1.0/24
let gateway = @10.0.1.1
let dnsServers = [@8.8.8.8, @10.0.1.2]

// Check gateway first
let gw = net.ping(gateway)
if (!gw.alive) {
    log.error($"Gateway {gateway} is DOWN!")
    sys.exit(1)
}

// Scan subnet for alive hosts
let results = []
for (let i in 1..255) {
    let host = @10.0.1.0 + i
    let ping = net.ping(host)

    if (ping.alive) {
        arr.push(results, HostStatus {
            ip: host,
            alive: true,
            latency: ping.latency,
            sshOpen: net.isPortOpen(host, 22),
            httpOpen: net.isPortOpen(host, 80)
        })
    }
}

// Report
io.println($"Alive hosts in {subnet}: {len(results)}")
for (let h in results) {
    let ssh = h.sshOpen ? "SSH✓" : "SSH✗"
    let http = h.httpOpen ? "HTTP✓" : "HTTP✗"
    io.println($"  {h.ip}  {h.latency}ms  {ssh}  {http}")
}

// DNS check
for (let dns in dnsServers) {
    let resolved = net.resolve("google.com")
    if (resolved != null) {
        io.println($"DNS {dns}: OK (google.com → {resolved})")
    } else {
        log.warn($"DNS {dns}: FAILED")
    }
}

// Check if critical servers are in the right subnet
let criticalServers = [@10.0.1.10, @10.0.1.11, @10.0.2.5]
for (let server in criticalServers) {
    if (server in subnet) {
        io.println($"  {server} ✓ in {subnet}")
    } else {
        log.warn($"  {server} ✗ NOT in {subnet}!")
    }
}
```

---

## 6. Implementation Phases

| Phase       | Scope                                                                                                      | Layer          |
| ----------- | ---------------------------------------------------------------------------------------------------------- | -------------- |
| **Phase 1** | IP literal syntax — lexer, parser, `StashIpAddress` type, `is ip`, string interpolation, equality          | Language-level |
| **Phase 2** | Operator integration — bitwise (`&`, `\|`, `~`), comparison (`<`, `>`), `in` (CIDR), arithmetic (`+`, `-`) | Language-level |
| **Phase 3** | `net` namespace — `net.resolve`, `net.ping`, `net.isPortOpen`, `net.interfaces`, `net.subnetInfo`          | Stdlib         |
| **Phase 4** | TCP/UDP — `net.tcpConnect`, `net.send`, `net.recv`, `net.udpSend`, `net.udpListen`                         | Stdlib         |

Phases 1–2 change the language core (lexer, parser, interpreter). Phases 3–4 are pure stdlib additions — no language grammar changes.

---

## 7. Design Decisions

### 7.1 Literal Syntax

**Decision:** `@` prefix — `@192.168.1.1`, `@::1`, `@10.0.0.0/24`.

The `@` sigil was chosen for the following reasons:

- **Semantic fit:** `@` already means "at an address" in networking contexts (SSH `user@host`, email `user@domain`). It reads naturally: "the address 192.168.1.1."
- **Minimal noise:** A single character prefix with no quotes needed. `@192.168.1.1` is lighter than `ip"192.168.1.1"`, `net"192.168.1.1"`, or `` `192.168.1.1` ``.
- **No conflicts:** `@` is unused in Stash syntax today. Stash does not have decorators (Python/Java) and any future annotation system would use C#-style `[Attribute]` syntax instead.
- **Unambiguous lexing:** The lexer sees `@` and enters a dedicated scan mode. No backtracking needed. The scan consumes hex digits, dots, colons (for IPv6), `/` (for CIDR), and `%` (for IPv6 zone IDs like `@fe80::1%eth0`), stopping at whitespace or any operator/delimiter.
- **IPv4 and IPv6:** Works cleanly for both — `@192.168.1.1`, `@::1`, `@fe80::1%eth0`, `@::ffff:10.0.0.1`.

**Alternatives considered:**
- `ip"..."` — functional but verbose, the quotes feel heavy for something that should be as light as a number literal.
- `` `...` `` — clean delimiters but backticks carry baggage from JS template literals and Bash command substitution.
- `#...` — minimal but `#` is strongly associated with comments in shell languages.
- `<...>` — conflicts with comparison operators `<` and `>`.
- Bare `192.168.1.1` — impossible due to lexer ambiguity with float literals (`10.0`) and integer-dot-integer sequences.

### 7.2 Value Equality

**Decision:** Yes — value equality.

Unlike structs/dicts (reference equality), IPs should use **value equality**: two IPs with the same address bytes are equal. This matches how strings, integers, and ranges behave. `==` compares bytes, not references.

### 7.3 CIDR as Part of the Type

**Decision:** Single type, optional CIDR.

`StashIpAddress` stores an optional `PrefixLength`. An IP with CIDR behaves as a subnet (supports `in` containment). An IP without CIDR behaves as a host address. Same type, different capabilities based on presence of prefix. This avoids type proliferation while keeping the API clean.

### 7.4 Bitwise on IPs

**Decision:** Yes — native bitwise on IP addresses.

`ip & ip` returns an IP (bitwise AND of the byte arrays). This makes subnet masking natural and is the primary reason sysadmins need bitwise operators in the first place. The interpreter's `VisitBinaryExpr` handles `StashIpAddress` operands alongside `long`.

### 7.5 `net` Namespace Scope

**Decision:** DNS, ping, ports, TCP, UDP, interfaces, subnet math.

WebSocket and MQTT are deferred — they're application-level protocols better served by packages. The `net` namespace focuses on IP-layer and transport-layer primitives that sysadmin scripts need constantly.
