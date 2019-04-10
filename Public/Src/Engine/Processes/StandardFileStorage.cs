// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Processes
{
    /// <summary>
    /// Implementation of <see cref="ISandboxedProcessFileStorage"/> based off <see cref="SandboxedProcessStandardFiles"/>.
    /// </summary>
    public class StandardFileStorage : ISandboxedProcessFileStorage
    {
        private readonly SandboxedProcessStandardFiles m_sandboxedProcessStandardFiles;

        /// <summary>
        /// Create an instance of <see cref="StandardFileStorage"/>.
        /// </summary>
        public StandardFileStorage(SandboxedProcessStandardFiles sandboxedProcessStandardFiles)
        {
            Contract.Requires(sandboxedProcessStandardFiles != null);
            m_sandboxedProcessStandardFiles = sandboxedProcessStandardFiles;
        }

        /// <inheritdoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            switch (file)
            {
                case SandboxedProcessFile.StandardError:
                    return m_sandboxedProcessStandardFiles.StandardError;
                case SandboxedProcessFile.StandardOutput:
                    return m_sandboxedProcessStandardFiles.StandardOutput;
                default:
                    Contract.Assert(false);
                    return null;
            }
        }
    }
}
