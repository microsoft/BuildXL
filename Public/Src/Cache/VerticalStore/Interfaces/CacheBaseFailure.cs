// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Base class for all cache related failures
    /// </summary>
    public abstract class CacheBaseFailure : Failure
    {
        /// <summary>
        /// Base failure type for the cache with optional inner failure
        /// </summary>
        /// <param name="innerFailure">Optional inner failure</param>
        protected CacheBaseFailure(Failure innerFailure = null)
            : base(innerFailure)
        {
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
