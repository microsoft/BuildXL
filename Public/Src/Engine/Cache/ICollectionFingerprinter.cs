// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Extension of <see cref="IFingerprinter"/> that can handle adding unnamed values in collections.
    /// </summary>
    public interface ICollectionFingerprinter : IFingerprinter
    {
        /// <summary>
        /// Adds an int to the fingerprint stream.
        /// </summary>
        void Add(int value);

        /// <summary>
        /// Adds a path to the fingerprint stream.
        /// </summary>
        void Add(AbsolutePath path);

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// </summary>
        void Add(StringId text);

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// </summary>
        void Add(string text);

        /// <summary>
        /// Adds a fingerprint to the fingerprint stream.
        /// </summary>
        void Add(Fingerprint fingerprint);

        /// <summary>
        /// Add name portion of path to the fingerprint stream.
        /// </summary>
        void AddFileName(AbsolutePath path);
    }
}
