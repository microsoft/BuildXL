// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

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
