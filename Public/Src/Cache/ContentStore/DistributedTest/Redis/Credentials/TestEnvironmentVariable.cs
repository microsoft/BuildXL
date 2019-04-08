// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
