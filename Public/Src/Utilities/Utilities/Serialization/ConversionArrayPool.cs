// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A special array pool version that pools the array of one element.
    /// Used for serialization purposes only.
    /// </summary>
    internal static class ConversionArrayPool
    {
        internal static class Array<T>
        {
            public static ArrayPool<T> ArrayPool = new ArrayPool<T>(1);
        }
    }
}