// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System.Collections.Generic.Enumerable
{
    interface IEnumerableIList<T> : IEnumerable<T>
    {
        new EnumeratorIList<T> GetEnumerator();
    }
}
