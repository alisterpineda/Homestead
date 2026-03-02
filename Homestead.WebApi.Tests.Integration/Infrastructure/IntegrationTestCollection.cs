namespace Homestead.WebApi.Tests.Integration.Infrastructure;

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<PostgresFixture>;
