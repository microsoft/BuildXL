// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Helper struct for constructing unique identifiers for a given folder names.
    /// </summary>
    public struct ReserveFoldersResolver
    {
        // The dictionary is allocated only when more than one folder name was queried for the unique id.
        // This saves reasonable amount of memory even for medium size builds (like .5Gb for Word).
        private StringId m_firstFolder;
        private int m_firstFolderCount;

        private readonly object m_syncRoot;
        private readonly Lazy<ConcurrentDictionary<StringId, int>> m_reservedFolders;

        /// <nodoc />
        public ReserveFoldersResolver(object syncRoot)
            : this()
        {
            m_syncRoot = syncRoot;

            m_reservedFolders = new Lazy<ConcurrentDictionary<StringId, int>>(() => new ConcurrentDictionary<StringId, int>(
                                                                                /*DEFAULT_CONCURRENCY_MULTIPLIER * PROCESSOR_COUNT*/
                                                                                concurrencyLevel: 4 * Environment.ProcessorCount,
                                                                                capacity: /*Default capacity*/ 4));
        }

        /// <summary>
        /// Returns the next Id for a given <paramref name="name"/>.
        /// </summary>
        public int GetNextId(PathAtom name)
        {
            Contract.Requires(name.IsValid);

            var id = name.StringId;

            // Check if we can use the first slot
            if (!m_firstFolder.IsValid)
            {
                lock (m_syncRoot)
                {
                    if (!m_firstFolder.IsValid)
                    {
                        m_firstFolder = id;
                        return 0;
                    }
                }
            }

            // Check if the name matches the first folder name.
            if (m_firstFolder == id)
            {
                return Interlocked.Increment(ref m_firstFolderCount);
            }

            // Fallback: using ConcurrentDictionary
            return m_reservedFolders.Value.AddOrUpdate(
                name.StringId,
                (_) => 0,
                (_, currentCount) => currentCount + 1);
        }
    }
}
