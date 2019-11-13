// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

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
        public const string DefaultToolRelativePath = @"SandboxedProcessExecutor.exe";

        /// <summary>
        /// Tool path.
        /// </summary>
        public string ExecutablePath { get; }

        /// <summary>
        /// Scopes that need to be untracked.
        /// </summary>
        public IEnumerable<string> UntrackedScopes { get; }

        /// <summary>
        /// Creates an instance of <see cref="ExternalToolSandboxedProcessExecutor"/>
        /// </summary>
        public ExternalToolSandboxedProcessExecutor(string executablePath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(executablePath));
            ExecutablePath = executablePath;

            if (!File.Exists(ExecutablePath))
            {
                throw new BuildXLException($"Cannot find file '{ExecutablePath}' needed to externally execute process. Did you build all configurations?", rootCause: ExceptionRootCause.MissingRuntimeDependency);
            }

            string directory = Path.GetDirectoryName(executablePath);

            // If the pip run by this sandboxed process executor tool also executes Detours, then that pip may access
            // DetoursServices.pdb that is linked with this tool. Thus, we need to tell the pip to untrack the directories
            // where DetoursServices are located.
            UntrackedScopes = new[] { Path.Combine(directory, "X86"), Path.Combine(directory, "X64") };
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
