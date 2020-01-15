// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System.Collections.Generic.Enumerable
{
    internal interface IEnumerableIList<T> : IEnumerable<T>
    {
        new EnumeratorIList<T> GetEnumerator();
    }
}
