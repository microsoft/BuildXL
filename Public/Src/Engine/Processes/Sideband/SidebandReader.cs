// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

using static BuildXL.Processes.Sideband.SidebandUtils;

namespace BuildXL.Processes
{
    /// <summary>
    /// A disposable reader for sideband files.
    /// 
    /// Uses <see cref="FileEnvelope"/> to check the integrity of the given file.
    /// </summary>
    public sealed class SidebandReader : IDisposable
    {
        private readonly BuildXLReader m_bxlReader;

        private int m_readCounter = 0;

        /// <nodoc />
        public SidebandReader(string sidebandFile)
        {
            Contract.Requires(File.Exists(sidebandFile));

            m_bxlReader = new BuildXLReader(
                stream: new FileStream(sidebandFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete),
                debug: false,
                leaveOpen: false);
        }

        /// <summary>
        /// Reads the header and returns if the sideband file has been compromised.
        /// 
        /// Even when the sideband file is compromised, it is possible to call <see cref="ReadRecordedPaths"/>
        /// which will then try to recover as many recorded paths as possible.
        /// </summary>
        public bool ReadHeader(bool ignoreChecksum)
        {
            AssertOrder(ref m_readCounter, cnt => cnt == 1, "Read header must be called first");
            var result = SidebandWriter.FileEnvelope.TryReadHeader(m_bxlReader.BaseStream, ignoreChecksum);
            return result.Succeeded;
        }

        /// <summary>
        /// Reads and returns the metadata.
        /// 
        /// Before calling this method, <see cref="ReadHeader(bool)"/> must be called first.
        /// </summary>
        public SidebandMetadata ReadMetadata()
        {
            AssertOrder(ref m_readCounter, cnt => cnt == 2, "ReadMetadata must be called second, right after ReadHeader");
            return SidebandMetadata.Deserialize(m_bxlReader);
        }

        /// <summary>
        /// Returns all paths recorded in this sideband file.
        /// 
        /// Before calling this method, <see cref="ReadHeader(bool)"/> and <see cref="ReadMetadata"/> must be called first.
        /// </summary>
        /// <remarks>
        /// Those paths are expected to be absolute paths of files/directories that were written to by the previous build.
        ///
        /// NOTE: this method does not validate the recorded paths in any way.  That means that each returned string may be
        ///   - a path pointing to an absent file
        ///   - a path pointing to a file
        ///   - a path pointing to a directory.
        /// 
        /// NOTE: if the sideband file was produced by an instance of this class (and wasn't corrupted in any way)
        ///   - the strings in the returned enumerable are all legal paths
        ///   - the returned collection does not contain any duplicates
        /// Whether or not this sideband file is corrupted is determined by the result of the <see cref="ReadHeader"/> method.
        /// </remarks>
        public IEnumerable<string> ReadRecordedPaths()
        {
            AssertOrder(ref m_readCounter, cnt => cnt > 2, "ReadRecordedPaths must be called after ReadHeader and ReadMetadata");
            string nextString = null;
            while ((nextString = ReadStringOrNull()) != null)
            {
                yield return nextString;
            }
        }

        private string ReadStringOrNull()
        {
            try
            {
                return m_bxlReader.ReadString();
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <nodoc />
        public void Dispose()
        {
            m_bxlReader.Dispose();
        }
    }
}
