// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// Interface for classes that can compute the BuildXL fingerprint.
    /// </summary>
    public interface IHashingHelper
    {
        /// <summary>
        /// Adds a fingerprint to the fingerprint stream.
        /// </summary>
        void Add(string name, Fingerprint fingerprint);

        /// <summary>
        /// Adds a content hash to the fingerprint stream.
        /// </summary>
        void Add(string name, ContentHash contentHash);

        /// <summary>
        /// Add the bytes from the string to the fingerprint stream.
        /// </summary>
        void Add(string name, string text);

        /// <summary>
        /// Adds an int to the fingerprint stream.
        /// </summary>
        void Add(string name, int value);

        /// <summary>
        /// Adds a long to the fingerprint stream.
        /// </summary>
        void Add(string name, long value);        
    }
}
