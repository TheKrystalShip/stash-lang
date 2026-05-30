using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// xUnit collection for registry authz tests that deliberately fire many concurrent
/// HTTP requests through a shared <c>WebApplicationFactory</c> + SQLite backing store
/// (the atomicity / claim-race suites). Marking the collection with
/// <c>DisableParallelization = true</c> forces every member to run serially — both with
/// respect to other members and to every other collection in the assembly.
///
/// Why this exists: these tests assert "exactly one winner / zero 500s" while issuing N
/// parallel requests. When OTHER registry test classes run concurrently in their own
/// collections, they contend on the same SQLite lock; the resulting transient
/// "database is locked" surfaces as a 500 inside the under-test request and the assertion
/// fails — a parallel-execution artifact, not a production fault. The class passes
/// reliably in isolation. Serializing the collection removes the cross-collection
/// contention without weakening the invariant being tested.
///
/// Apply <c>[Collection("RegistryConcurrency")]</c> to any registry test class that issues
/// concurrent requests and asserts on row counts / absence of 500s.
///
/// Precedent: <c>Stash.Tests/Common/SystemCwdCollection.cs</c> (same DisableParallelization
/// remedy for a process-global contention flake).
/// </summary>
[CollectionDefinition("RegistryConcurrency", DisableParallelization = true)]
public class RegistryConcurrencyCollection
{
}
