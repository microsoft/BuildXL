// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace ContentStoreTest.Distributed.Redis.Credentials
{
    /// <summary>
    ///     Test helper class for setting an environment variable within a scope
    /// </summary>
    public sealed class TestEnvironmentVariable : IDisposable
    {
        private readonly string _variableName;

        public TestEnvironmentVariable(string variableName, string value)
        {
            _variableName = variableName;

            Environment.SetEnvironmentVariable(_variableName, value);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_variableName, string.Empty);
        }
    }
}
