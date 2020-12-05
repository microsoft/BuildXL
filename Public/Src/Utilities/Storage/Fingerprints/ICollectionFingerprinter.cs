// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Storage.Fingerprints
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
        /// Adds name portion of path to the fingerprint stream.
        /// </summary>
        void AddFileName(AbsolutePath path);

        /// <summary>
        /// Adds a file name to the fingerprint stream.
        /// </summary>
        void AddFileName(StringId fileName);
    }
}
