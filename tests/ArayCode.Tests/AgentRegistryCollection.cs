using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Collection definition for test classes that modify the shared static
/// <see cref="AgentRegistry"/> state. Tests in this collection run
/// sequentially to prevent race conditions on the shared mutable state.
/// </summary>
[CollectionDefinition("AgentRegistryCollection", DisableParallelization = true)]
public class AgentRegistryCollection
{
}
