// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Cache.BasicFilesystem
{
    internal sealed class BasicFilesystemCacheSessionFailure : Failure
    {
        string Message { get; }

        public BasicFilesystemCacheSessionFailure(string message)
        {
            Message = message;
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Message);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return Message;
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
