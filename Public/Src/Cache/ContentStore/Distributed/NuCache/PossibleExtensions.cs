// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public static class PossibleExtensions
    {
        /// <summary>
        /// Convert BuildXL "possible" into a cache "possible".
        /// </summary>
        public static BoolResult ToBoolResult(this Possible<Unit> possible)
        {
            if (possible.Succeeded)
            {
                return BoolResult.Success;
            }

            return new BoolResult(possible.Failure.DescribeIncludingInnerFailures());
        }

        /// <summary>
        /// Throws an error if the possible has a failure.
        /// </summary>
        public static T ThrowOnError<T>(this Possible<T> possible)
        {
            if (!possible.Succeeded)
            {
                throw possible.Failure.CreateException();
            }

            return possible.Result;
        }
    }
}
