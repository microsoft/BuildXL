// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Convenience extensions methods for numbers.
    /// </summary>
    public static class NumberExtensions
    {
        /// <summary>
        ///     Convert free percent of a max size to a used size.
        /// </summary>
        public static long PercentFreeToUsedSize(this long percentFree, long maxSize)
        {
            Contract.Requires(percentFree >= 0);
            Contract.Requires(percentFree <= 100);

            return (100 - percentFree) * maxSize / 100;
        }
    }
}
