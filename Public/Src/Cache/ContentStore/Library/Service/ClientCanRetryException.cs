// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     An exception meaning a retry of the operation may succeed.
    /// </summary>
    public class ClientCanRetryException : CacheException
    {
        /// <summary>
        ///     Creator's context that can be used for logging this exception in a context where this info is not available.
        /// </summary>
        public readonly Context Context;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCanRetryException"/> class.
        /// </summary>
        public ClientCanRetryException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCanRetryException"/> class.
        /// </summary>
        public ClientCanRetryException(Context context, string message)
            : base(message)
        {
            Context = context;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCanRetryException"/> class.
        /// </summary>
        public ClientCanRetryException(Context context, string message, Exception innerException)
            : base($"{message}: {innerException}", innerException)
        {
            Context = context;
        }
    }
}
