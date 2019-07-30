// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Redis.Credentials
{
    [Trait("Category", "Integration")]
    [Collection("ConnectionStringProviderTests")] // Serialized so that environment variable use doesn't overlap.
    public class ExecutableConnectionStringProviderTests : TestBase
    {
        private const ExecutableConnectionStringProvider.RedisConnectionIntent Intent =
            ExecutableConnectionStringProvider.RedisConnectionIntent.Content;

        private const string ConnectionString = "ConnectionString1234";

#if PLATFORM_WIN
        private const string ConnectionStringProviderScript = @"@echo off

IF /I ""%1"" EQU """" (
    echo ""-intent flag must be provided as first argument""
    exit /b 1
)

IF /I ""%2"" EQU """" (
    echo ""-intent value must be provided as second argument""
    exit /b 1
)

echo " + ConnectionString;
#else
        private const string ConnectionStringProviderScript = @"#!/bin/bash

if [[ ""$1"" == """" ]]; then
    echo ""-intent flag must be provided as first argument""
    exit 1
fi

if [[ ""$2"" == """" ]]; then
    echo ""-intent value must be provided as second argument""
    exit 2
fi

echo " + ConnectionString;
#endif

        public ExecutableConnectionStringProviderTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        // TODO: User Story 1492406: Enable ExecutableConnectionStringProviderTests for macOS
        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public async Task GetConnectionString()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var providerPath = testDirectory.Path / "provider.bat";
                FileSystem.WriteAllBytes(providerPath, Encoding.UTF8.GetBytes(ConnectionStringProviderScript));
                using (new TestEnvironmentVariable(ExecutableConnectionStringProvider.CredentialProviderVariableName, providerPath.Path))
                {
                    var provider = new ExecutableConnectionStringProvider(Intent);
                    var connectionStringResult = await provider.GetConnectionString();
                    connectionStringResult.Succeeded.Should().BeTrue(connectionStringResult.ToString());
                    connectionStringResult.ConnectionString.Should().Be(ConnectionString);
                }
            }
        }

        // TODO: User Story 1492406: Enable ExecutableConnectionStringProviderTests for macOS
        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public async Task NonexistentExecutableConnectionStringProvider()
        {
            var nonexistentProviderPath = Path.Combine(Path.GetTempPath(), "nonexistentProvider.exe");
            using (new TestEnvironmentVariable(ExecutableConnectionStringProvider.CredentialProviderVariableName, nonexistentProviderPath))
            {
                var provider = new ExecutableConnectionStringProvider(Intent);
                var connectionStringResult = await provider.GetConnectionString();
                connectionStringResult.Succeeded.Should().BeFalse();
                connectionStringResult.ErrorMessage.Should().Contain("The system cannot find the file specified");
            }
        }
    }
}
