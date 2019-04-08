// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
