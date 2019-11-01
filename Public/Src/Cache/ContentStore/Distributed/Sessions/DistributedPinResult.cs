// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <nodoc />
    public sealed class DistributedPinResult : PinResult
    {
        private readonly string _extraSuccessMessage;

        /// <summary>
        /// True when the remote pin succeeded after the content was copied locally.
        /// </summary>
        public bool CopyLocally { get; private set; }

        private DistributedPinResult(string extraSuccessMessage)
            : base(ResultCode.Success)
        {
            _extraSuccessMessage = extraSuccessMessage;
        }

        /// <nodoc />
        public DistributedPinResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <nodoc />
        public static DistributedPinResult SuccessByLocalCopy()
        {
            return new DistributedPinResult("Copied locally")
                   {
                       CopyLocally = true,
                   };
        }

        /// <nodoc />
        public static new DistributedPinResult Success(string message)
        {
            return new DistributedPinResult(message);
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return $"Success({_extraSuccessMessage})";
        }
    }
}
