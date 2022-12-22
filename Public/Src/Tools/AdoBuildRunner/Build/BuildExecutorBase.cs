// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
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
            Logger = logger;
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
