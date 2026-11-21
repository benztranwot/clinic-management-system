// Each factory owns a database; bound concurrent work on the shared LocalDB engine.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]

namespace HealthcareApp.Api.IntegrationTests;

// Run timing measurements without concurrent tests competing for LocalDB.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScaleMeasurementCollection
{
    public const string Name = "Scale measurements";
}
