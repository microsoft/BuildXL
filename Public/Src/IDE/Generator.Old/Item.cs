// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Ide.Generator.Old
{
    /// <summary>
    /// Item
    /// </summary>
    public sealed class Item
    {
        private readonly ConcurrentDictionary<string, object> m_metadata;

        /// <summary>
        /// Constructs a new item
        /// </summary>
        internal Item(object include)
        {
            Include = include;
            m_metadata = new ConcurrentDictionary<string, object>();
        }

        /// <summary>
        /// The include for this item.
        /// </summary>
        public object Include { get; private set; }

        /// <summary>
        /// Returns all metadata items
        /// </summary>
        public IEnumerable<KeyValuePair<string, object>> Metadata => m_metadata;

        /// <summary>
        /// Sets a piece of metadata
        /// </summary>
        public void SetMetadata(string key, string value)
        {
            Contract.Requires(!string.IsNullOrEmpty(key));
            Contract.Requires(value != null);

            m_metadata[key] = value;
        }

        /// <summary>
        /// Sets a piece of metadata
        /// </summary>
        public void SetMetadata(string key, AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(key));
            Contract.Requires(path != AbsolutePath.Invalid);

            m_metadata[key] = path;
        }

        /// <summary>
        /// Sets a piece of metadata
        /// </summary>
        public void SetMetadata(string key, RelativePath path)
        {
            Contract.Requires(!string.IsNullOrEmpty(key));
            Contract.Requires(path != RelativePath.Invalid);

            m_metadata[key] = path;
        }

        /// <summary>
        /// Sets a piece of metadata
        /// </summary>
        public void SetMetadata(string key, PathAtom path)
        {
            Contract.Requires(!string.IsNullOrEmpty(key));
            Contract.Requires(path != PathAtom.Invalid);

            m_metadata[key] = path;
        }
    }
}
