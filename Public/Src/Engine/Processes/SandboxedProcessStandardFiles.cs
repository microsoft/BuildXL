// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Files potentially created by the sandboxed process.
    /// </summary>
    public class SandboxedProcessStandardFiles
    {
        /// <summary>
        /// Standard output.
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// Standard error.
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// Creates an instance of <see cref="SandboxedProcessFile"/>.
        /// </summary>
        public SandboxedProcessStandardFiles(string standardOutput, string standardError)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(standardOutput));
            Contract.Requires(!string.IsNullOrWhiteSpace(standardError));

            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        /// <summary>
        /// Serializes this instance into the given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(StandardOutput);
            writer.Write(StandardError);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessStandardFiles"/>.
        /// </summary>
        public static SandboxedProcessStandardFiles Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            string output = reader.ReadString();
            string error = reader.ReadString();

            return new SandboxedProcessStandardFiles(output, error);
        }

        /// <summary>
        /// Creates an instance of <see cref="SandboxedProcessStandardFiles"/> from <see cref="ISandboxedProcessFileStorage"/>.
        /// </summary>
        public static SandboxedProcessStandardFiles From(ISandboxedProcessFileStorage fileStorage) => 
            new SandboxedProcessStandardFiles(
                fileStorage.GetFileName(SandboxedProcessFile.StandardOutput), 
                fileStorage.GetFileName(SandboxedProcessFile.StandardError));
    }
}
