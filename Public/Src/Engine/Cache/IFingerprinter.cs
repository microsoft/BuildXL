// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Interface for classes that can compute the BuildXL fingerprint.
    /// </summary>
    public interface IFingerprinter
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

        /// <summary>
        /// Adds a path to the fingerprint stream.
        /// </summary>
        void Add(string name, AbsolutePath path);

        /// <summary>
        /// Adds a string to the fingerprint stream.
        /// </summary>
        void Add(string name, StringId text);

        /// <summary>
        /// Adds a path and content hash pair to the fingerprint stream.
        /// </summary>
        void Add(AbsolutePath path, ContentHash hash);

        /// <summary>
        /// Adds a path and fingerprint pair to the fingerprint stream.
        /// </summary>
        void Add(AbsolutePath path, Fingerprint fingerprint);

        /// <summary>
        /// Adds a path and content hash pair to the fingerprint stream.
        /// </summary>
        void Add(string name, AbsolutePath path, ContentHash hash);

        /// <summary>
        /// Adds a path and fingerprint pair to the fingerprint stream.
        /// </summary>
        void Add(string name, AbsolutePath path, Fingerprint fingerprint);

        /// <summary>
        /// Adds all the elements specified in <paramref name="addOps"/> under a named, nested scope.
        /// </summary>
        void AddNested(string name, Action<IFingerprinter> addOps);

        /// <summary>
        /// Adds all the elements specified in <paramref name="addOps"/> under a named, nested scope.
        /// </summary>
        void AddNested(StringId name, Action<IFingerprinter> addOps);

        /// <summary>
        /// Adds all the elements specified in <paramref name="addOps"/> under a path, nested scope.
        /// </summary>
        void AddNested(AbsolutePath path, Action<IFingerprinter> addOps);

        /// <summary>
        /// Adds a collection to the fingerprint stream. <paramref name="addElement"/> is called per element,
        /// and should add the element to the fingerprint stream. If the given collection is null, it is
        /// treated as if it were non-null with a zero length.
        /// </summary>
        /// <Note>
        /// The collection of elements is passed in as a generic that is constrained to an interface rather than directly as an interface to
        /// prevent the C# compiler from boxing any structs that may be passed in for the collection.
        /// </Note>
        void AddCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement) 
            where TCollection : IEnumerable<TValue>;

        /// <summary>
        /// Combine and hash the elements of the collections regardless of the order
        /// </summary>
        void AddOrderIndependentCollection<TValue, TCollection>(string name, TCollection elements, Action<ICollectionFingerprinter, TValue> addElement, IComparer<TValue> comparer)
            where TCollection : IEnumerable<TValue>;
    }
}
