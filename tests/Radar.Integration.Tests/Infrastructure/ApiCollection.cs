
namespace Radar.Integration.Tests.Infrastructure
{
    [CollectionDefinition(Name)]
    public sealed class ApiCollection : ICollectionFixture<RadarApiFactory>
    {
        public const string Name = "Api";
    }
}
