// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Stdout and stderr stream redirection to files or trace file, potentially created by the sandboxed process.
    /// </summary>
    public class SandboxedProcessStandardFiles
    {
        /// <summary>
        /// Standard output redirected file path.
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// Standard error redirected file path.
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// Sandbox trace file.
        /// </summary>
        public string Trace { get; }

        /// <summary>
        /// Creates an instance of <see cref="SandboxedProcessFile"/>.
        /// </summary>
        public SandboxedProcessStandardFiles(string standardOutput, string standardError, string trace)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(standardOutput));
            Contract.Requires(!string.IsNullOrWhiteSpace(standardError));

            StandardOutput = standardOutput;
            StandardError = standardError;
            Trace = trace;
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
        /// Serializes this instance into the given <paramref name="writer"/> including the trace file.
        /// </summary>
        /// <remarks>
        /// This method is a temporary method used to prevent breaking change when process remoting is enabled.
        /// An instance of this class is serialized/deserialized in the SandboxedProcessResult as part of SandboxedProcessOutput.
        /// The problem is in the serialization format of SandboxedProcessOutput the instance of this class sits in the middle.
        /// If BuildXL serialization include the trace file, but AnyBuild deserialization does not include the trace file,
        /// then the serialization of SandboxedProcessOutput will fail. 
        /// TODO: SerializeForTrace and DeserializeForTrace should become Serialize and Deserialize, and the serialization method of SandboxedProcessOutput
        ///       should be simplified.
        ///       This requires temporarily breaking process remoting with AnyBuild, and update AnyBuild with new BuildXL.
        ///       See bug #1989497 for tracking.
        /// </remarks>
        public void SerializeForTrace(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(StandardOutput);
            writer.Write(StandardError);
            writer.WriteNullableString(Trace);
        }

        /// <summary>
        /// Serializes an empty instance into the given <paramref name="writer"/>.
        /// </summary>
        public static void SerializeEmpty(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(string.Empty);  // StandardOutput
            writer.Write(string.Empty);  // StandardError

            // TODO: The test Test.BuildXL.Processes.Detours.SandboxedProcessInfoTest.SerializeSandboxedProcessInfo(useNullFileStorage: True, useRootJail: True)
            //       failed when the following statement is uncommented because the deserialization would not have context about the trace file.
            //       This method is currently only be used in test. We need to uncomment the statement once we resolve bug #1989497
            // writer.Write(string.Empty);  // SandboxTrace
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessStandardFiles"/>.
        /// </summary>
        public static SandboxedProcessStandardFiles Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            string output = reader.ReadString();
            string error = reader.ReadString();

            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            return new SandboxedProcessStandardFiles(output, error, null);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessStandardFiles"/> including the trace.
        /// </summary>
        public static SandboxedProcessStandardFiles DeserializeForTrace(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            string output = reader.ReadString();
            string error = reader.ReadString();
            string trace = reader.ReadNullableString();

            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            return new SandboxedProcessStandardFiles(output, error, trace);
        }

        /// <summary>
        /// Creates an instance of <see cref="SandboxedProcessStandardFiles"/> from <see cref="ISandboxedProcessFileStorage"/>.
        /// </summary>
        public static SandboxedProcessStandardFiles From(ISandboxedProcessFileStorage fileStorage) => 
            new SandboxedProcessStandardFiles(
                fileStorage.GetFileName(SandboxedProcessFile.StandardOutput), 
                fileStorage.GetFileName(SandboxedProcessFile.StandardError),
                fileStorage.GetFileName(SandboxedProcessFile.Trace));
    }
}
