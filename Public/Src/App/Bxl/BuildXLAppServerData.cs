// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.App.Tracing;
using BuildXL.Utilities;

namespace BuildXL
{
    /// <summary>
    /// Handshake data exchanged by BuildXL server and client.
    /// </summary>
    public struct BuildXLAppServerData
    {
        /// <summary>
        /// Current directory.
        /// </summary>
        /// <remarks>
        /// The current directory may change build to build, and thus it needs to get reflected in the server.
        /// </remarks>
        public string CurrentDirectory { get; }

        /// <summary>
        /// List of environment variables and their values.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> EnvironmentVariables { get; }

        /// <summary>
        /// Raw arguments.
        /// </summary>
        public IReadOnlyList<string> RawArgs { get; }

        /// <summary>
        /// Status and perf. data of server mode.
        /// </summary>
        /// <remarks>
        /// The logging information related to server mode, so the server can log it in its own context.
        /// </remarks>
        public ServerModeStatusAndPerf ServerModeStatusAndPerf { get; }

        /// <summary>
        /// Client path.
        /// </summary>
        /// <remarks>
        /// Client path is needed so that it can be properly logged on server.
        /// </remarks>
        public string ClientPath { get; }

        /// <summary>
        /// Client start time.
        /// </summary>
        public DateTime ClientStartTime { get; }

        /// <summary>
        /// Creates an instance of <see cref="BuildXLAppServerData"/>.
        /// </summary>
        public BuildXLAppServerData(
            IReadOnlyList<string> rawArgs,
            IReadOnlyList<KeyValuePair<string, string>> environmentVariables,
            ServerModeStatusAndPerf serverModeStatusAndPerf,
            string currentDirectory,
            string clientPath,
            DateTime clientStartTime)
        {
            Contract.Requires(rawArgs != null);
            Contract.Requires(environmentVariables != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(currentDirectory));
            Contract.Requires(!string.IsNullOrWhiteSpace(clientPath));

            RawArgs = rawArgs;
            EnvironmentVariables = environmentVariables;
            ServerModeStatusAndPerf = serverModeStatusAndPerf;
            CurrentDirectory = currentDirectory;
            ClientPath = clientPath;
            ClientStartTime = clientStartTime;
        }

        /// <summary>
        /// Creates an instance of <see cref="BuildXLAppServerData"/>.
        /// </summary>
        public static BuildXLAppServerData Create(
             IReadOnlyList<string> rawArgs,
             IReadOnlyList<KeyValuePair<string, string>> environmentVariables,
             ServerModeStatusAndPerf serverModeStatusAndPerf)
        {
            Contract.Requires(rawArgs != null);
            Contract.Requires(environmentVariables != null);

            return new BuildXLAppServerData(
                rawArgs,
                environmentVariables,
                serverModeStatusAndPerf,
                Directory.GetCurrentDirectory(),
                AssemblyHelper.GetThisProgramExeLocation(),
                Process.GetCurrentProcess().StartTime);
        }

        /// <summary>
        /// Serializes this instance.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RawArgs.Count);

            foreach (string arg in RawArgs)
            {
                writer.Write(arg);
            }

            writer.Write(ClientPath);
            writer.Write(ClientStartTime.ToFileTime());

            writer.Write(EnvironmentVariables.Count);

            foreach (KeyValuePair<string, string> variable in EnvironmentVariables)
            {
                writer.Write(variable.Key);
                writer.Write(variable.Value);
            }

            writer.Write(CurrentDirectory);
            ServerModeStatusAndPerf.Write(writer);

            writer.Flush();
        }

        /// <summary>
        /// Deserilizes data and creates an instance of <see cref="BuildXLAppServerData"/>.
        /// </summary>
        public static BuildXLAppServerData Deserialize(BinaryReader reader)
        {
            int numberOfArgs = reader.ReadInt32();
            Contract.Assert(numberOfArgs >= 0);

            var rawArgs = new string[numberOfArgs];

            for (int i = 0; i < rawArgs.Length; i++)
            {
                rawArgs[i] = reader.ReadString();
            }

            var clientPath = reader.ReadString();
            var clientStartTime = DateTime.FromFileTime(reader.ReadInt64()).ToUniversalTime();

            int environmentVariableCount = reader.ReadInt32();
            var environmentVariables = new KeyValuePair<string, string>[environmentVariableCount];
            for (int i = 0; i < environmentVariableCount; i++)
            {
                environmentVariables[i] = new KeyValuePair<string, string>(reader.ReadString(), reader.ReadString());
            }

            var currentDirectory = reader.ReadString();
            var serverModeStatusAndPerf = ServerModeStatusAndPerf.Read(reader);

            return new BuildXLAppServerData(
                rawArgs,
                environmentVariables,
                serverModeStatusAndPerf,
                currentDirectory,
                clientPath,
                clientStartTime);
        }
    }
}
