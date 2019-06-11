// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Processes
{
    /// <summary>
    /// Class that wraps the tool for executing sandboxed process externally.
    /// </summary>
    public class ExternalToolSandboxedProcessExecutor
    {
        /// <summary>
        /// Relative path to the default tool.
        /// </summary>
        public const string DefaultToolRelativePath = @"tools\SandboxedProcessExecutor\SandboxedProcessExecutor.exe";

        /// <summary>
        /// Tool path.
        /// </summary>
        public string ExecutablePath { get; }

        /// <summary>
        /// Creates an instance of <see cref="ExternalToolSandboxedProcessExecutor"/>
        /// </summary>
        public ExternalToolSandboxedProcessExecutor(string executablePath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(executablePath));
            ExecutablePath = executablePath;
        }

        /// <summary>
        /// Creates arguments for the tool to execute.
        /// </summary>
        public string CreateArguments(string sandboxedProcessInfoInputFile, string sandboxedProcessResultOutputFile)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sandboxedProcessInfoInputFile));
            Contract.Requires(!string.IsNullOrWhiteSpace(sandboxedProcessResultOutputFile));

            return $"/sandboxedProcessInfo:\"{sandboxedProcessInfoInputFile}\" /sandboxedProcessResult:\"{sandboxedProcessResultOutputFile}\"";
        }
    }
}
