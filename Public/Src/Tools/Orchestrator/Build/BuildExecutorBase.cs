// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Orchestrator.Vsts;

namespace BuildXL.Orchestrator.Build
{
    /// <summary>
    /// A build executor base class with common properties
    /// </summary>
    public abstract class BuildExecutorBase
    {
        /// <nodoc />
        protected readonly ILogger Logger;

        /// <nodoc />
        protected BuildExecutorBase(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Used to set environment variables on a build agent
        /// </summary>
        /// <param name="name">Environment variable name</param>
        /// <param name="value">Environment variable value</param>
        protected void SetEnvVar(string name, string value)
        {
            Logger.Info($@"Setting env var {name}={value}");
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
