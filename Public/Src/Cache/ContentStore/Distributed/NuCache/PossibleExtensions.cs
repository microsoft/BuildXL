// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tasks;

#nullable enable

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
