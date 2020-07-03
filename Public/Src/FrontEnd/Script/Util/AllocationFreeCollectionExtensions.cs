// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Script.Util
{
    internal static class AllocationFreeCollectionExtensions
    {
        public static bool Any<T>(this IReadOnlyList<T> list, Func<T, bool> predicate)
        {
            foreach (var e in list.AsStructEnumerable())
            {
                if (predicate(e))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
