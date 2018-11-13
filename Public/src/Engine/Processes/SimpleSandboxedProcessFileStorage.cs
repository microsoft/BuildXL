// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Processes
{
    /// <summary>
    /// A very simple implementation of <see cref="ISandboxedProcessFileStorage"/>, based on a
    /// known root directory, known upfront.
    /// </summary>
    public class SimpleSandboxedProcessFileStorage : ISandboxedProcessFileStorage
    {
        private readonly string m_directory;

        /// <nodoc />
        public SimpleSandboxedProcessFileStorage(string directory)
        {
            Contract.Requires(!string.IsNullOrEmpty(directory));
            m_directory = directory;
        }

        /// <inheritdoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(m_directory, file.DefaultFileName());
        }
    }
}
