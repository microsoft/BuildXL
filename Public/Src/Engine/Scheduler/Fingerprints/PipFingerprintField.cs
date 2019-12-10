// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Struct naming common fields in pip fingerprint.
    /// </summary>
    public struct PipFingerprintField
    {
        /// <nodoc/>
        public const string ExecutionAndFingerprintOptionsHash = nameof(ExecutionAndFingerprintOptionsHash);

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
