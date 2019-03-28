// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Redis.Credentials
{
    [Collection("ConnectionStringProviderTests")] // Serialized so that environment variable use doesn't overlap.
    public class EnvironmentConnectionStringProviderTests
    {
        [Fact]
        public async Task GetConnectionStringEmpty()
        {
            var variableName = $"TestEnvVar{ThreadSafeRandom.Generator.Next()}";
            var provider = new EnvironmentConnectionStringProvider(variableName);
            var connectionStringResult = await provider.GetConnectionString();
            connectionStringResult.Succeeded.Should().BeFalse();
            connectionStringResult.ErrorMessage.Should().Contain($"Expected connection string environment variable not defined: [{variableName}]'");
        }

        [Fact]
        public async Task GetConnectionString()
        {
            var variableName = $"TestEnvVar{ThreadSafeRandom.Generator.Next()}";
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            var provider = new EnvironmentConnectionStringProvider(variableName);
            using (new TestEnvironmentVariable(variableName, connectionString))
            {
                var connectionStringResult = await provider.GetConnectionString();
                connectionStringResult.Succeeded.Should().BeTrue();
                connectionStringResult.ConnectionString.Should().Be(connectionString);
            }
        }
    }
}
