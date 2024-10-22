// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildToolsInstaller.Tests
{
    /// <summary>
    /// Use this disposable object to modify the environment and restore it on dispose
    /// </summary>
    internal sealed class TemporaryTestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> m_originalValues = new();
        public TemporaryTestEnvironment() { }

        public void Set(string variable, string? value)
        {
            if (!m_originalValues.ContainsKey(variable))
            {
                m_originalValues[variable] = Environment.GetEnvironmentVariable(variable);
            }

            Environment.SetEnvironmentVariable(variable, value);
        }
        
        /// <nodoc />
        public void Dispose()
        {
            foreach (var key in m_originalValues.Keys)
            {
                Environment.SetEnvironmentVariable(key, m_originalValues[key]);
            }
        }
    }
}
