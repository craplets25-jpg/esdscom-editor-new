using Xunit;

namespace eSDSCom.Editor.Tests;

// Ensures DB-backed integration tests don't run in parallel against the same database.
[CollectionDefinition("BrokerTests", DisableParallelization = true)]
public class BrokerTestsCollection : ICollectionFixture<DatabaseFixture>
{
}
