// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Failure describing that the file was not produced because the file already exists.
    /// Note that the MemoizationStoreAdapter is hardcoded to use the <see cref="BuildXL.Cache.ContentStore.Interfaces.Sessions.FileReplacementMode.FailIfExists" />
    /// option because it assumes that BuildXL deletes any existing files before placement.
    /// </summary>
    public sealed class FileAlreadyExistsFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;
        private readonly string m_filename;

        /// <summary>
        /// Create the failure, including the CasHash and the target filename that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="casHash">The CasHash that failed</param>
        /// <param name="filename">The filename that failed</param>
        public FileAlreadyExistsFailure(string cacheId, CasHash casHash, string filename)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(filename != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
            m_filename = filename;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] did not produce file [{1}] from the CasHash entry [{2}] because a file at that path already exists.", m_cacheId, m_filename, m_casHash);
        }
    }
}
