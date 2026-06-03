using Xunit;

namespace Stash.Tests.Common;

/// <summary>
/// xUnit collection for tests that bind a <see cref="System.Net.HttpListener"/> (or any
/// loopback socket) to serve HTTP during the test. Marking the collection with
/// <c>DisableParallelization = true</c> forces every member to run serially — both with
/// respect to other members and to every other collection in the assembly.
///
/// Why this exists: the loopback TCP port space is a process-/host-global resource. Even with
/// a probe-a-free-port-then-retry-on-conflict loop, two test classes binding listeners
/// concurrently under full-suite load can still collide (a port freed by one probe is reclaimed
/// by another before <c>HttpListener.Start()</c> binds it), producing an intermittent
/// <c>HttpListenerException: Address already in use</c> that is green in isolation and red under
/// load. Serializing the listener tests removes the cross-collection contention entirely; the
/// in-test retry loop remains as defense in depth.
///
/// Apply <c>[Collection("NetworkListenerTests")]</c> to every test class that starts a local
/// HTTP listener (e.g. via a <c>StartTestServer</c> helper).
/// </summary>
[CollectionDefinition("NetworkListenerTests", DisableParallelization = true)]
public class NetworkListenerCollection
{
}
