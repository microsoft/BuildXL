// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Parameters for process execution.
    /// </summary>
    /// <remarks>
    /// CODESYNC: (AnyBuild) src/Client/ClientLibNetStd/Shim/RemoteCommandExecutionInfo.cs
    /// </remarks>
    public sealed class RemoteCommandExecutionInfo
    {
        /// <nodoc/>
        public RemoteCommandExecutionInfo(
            string command,
            string? args,
            string cwd,
            bool useLocalEnvironment,
            IEnumerable<KeyValuePair<string, string>>? environmentVariablesToAdd)
        {
            Command = command;
            Args = args ?? string.Empty;
            WorkingDirectory = cwd;
            var env = new Dictionary<string, string>(OperatingSystemHelper.EnvVarComparer);
            if (useLocalEnvironment)
            {
#pragma warning disable CS8605
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8604
                foreach (DictionaryEntry kvp in Environment.GetEnvironmentVariables())
                {
                    env[kvp.Key.ToString()] = kvp.Value.ToString();
                }
#pragma warning restore CS8604
#pragma warning restore CS8602
#pragma warning restore CS8601
#pragma warning restore CS8605

            }

            if (environmentVariablesToAdd != null)
            {
                foreach (KeyValuePair<string, string> kvp in environmentVariablesToAdd)
                {
                    env[kvp.Key] = kvp.Value;
                }
            }

            Env = env;
        }

        /// <nodoc/>
        public string Command { get; }

        /// <nodoc/>
        public string Args { get; }

        /// <nodoc/>
        public string WorkingDirectory { get; }

        /// <nodoc/>
        public IReadOnlyDictionary<string, string> Env { get; }
    }
}
