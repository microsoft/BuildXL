// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// This object represents the results of scheduling the process pip. It contains all the final artifacts to be used
    /// for dependency chaining.
    /// This object is wrapped inside a DScript object and handed out to users.
    /// </summary>
    public sealed class ProcessOutputs
    {
        private readonly Dictionary<AbsolutePath, FileArtifactWithAttributes> m_outputFileMap;
        private readonly Dictionary<AbsolutePath, StaticDirectory> m_outputDirectoryMap;

        /// <nodoc />
        public PipId ProcessPipId { get; internal set; }

        /// <nodoc />
        public ProcessOutputs(Dictionary<AbsolutePath, FileArtifactWithAttributes> outputFileMap, Dictionary<AbsolutePath, StaticDirectory> outputDirectoryMap)
        {
            m_outputFileMap = outputFileMap;
            m_outputDirectoryMap = outputDirectoryMap;

            ProcessPipId = PipId.Invalid;
        }

        /// <nodoc />
        public bool TryGetOutputFile(AbsolutePath path, out FileArtifact file)
        {
            if (m_outputFileMap.TryGetValue(path, out var fileWithAttributes))
            {
                file = fileWithAttributes.ToFileArtifact();
                return true;
            }

            file = default;
            return false;
        }

        /// <nodoc />
        public bool TryGetOutputDirectory(AbsolutePath path, out StaticDirectory staticDirectory)
        {
            return m_outputDirectoryMap.TryGetValue(path, out staticDirectory);
        }

        /// <nodoc />
        public IEnumerable<FileArtifact> GetOutputFiles()
        {
            return m_outputFileMap.Values
                .Select(fileWithAttributes => fileWithAttributes.ToFileArtifact());
        }

        /// <nodoc />
        public IEnumerable<FileArtifact> GetRequiredOutputFiles()
        {
            return m_outputFileMap.Values
                .Where(fileWithAttributes => fileWithAttributes.IsRequiredOutputFile)
                .Select(fileWithAttributes => fileWithAttributes.ToFileArtifact());
        }

        /// <nodoc />
        public IEnumerable<StaticDirectory> GetOutputDirectories()
        {
            return m_outputDirectoryMap.Values;
        }
    }
}
