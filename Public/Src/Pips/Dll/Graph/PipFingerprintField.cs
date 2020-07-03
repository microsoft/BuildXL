// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Struct naming common fields in pip fingerprint.
    /// </summary>
    public struct PipFingerprintField
    {
        /// <nodoc/>
        public const string ExecutionAndFingerprintOptions = nameof(ExecutionAndFingerprintOptions);       

        /// <nodoc/>
        public const string ContentHashAlgorithmName = nameof(ContentHashAlgorithmName);

        /// <nodoc/>
        public const string PipType = nameof(PipType);

        /// <nodoc/>
        public struct Process
        {
            /// <nodoc/>
            public const string SourceChangeAffectedInputList = nameof(SourceChangeAffectedInputList);
        }

        /// <nodoc/>
        public struct FileDependency
        {
            /// <nodoc/>
            public const string PathNormalizedWriteFileContent = nameof(PathNormalizedWriteFileContent);
        }

        /// <nodoc/>
        public struct FileOutput
        {
            /// <nodoc/>
            public const string Attributes = nameof(Attributes);
        }
    }
}
