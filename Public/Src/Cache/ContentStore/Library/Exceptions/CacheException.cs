// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Runtime.Serialization;

namespace BuildXL.Cache.ContentStore.Exceptions
{
    /// <summary>
    ///     Base class for all exceptions originating from the cache.
    /// </summary>
    [Serializable]
    public class CacheException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class.
        /// </summary>
        public CacheException()
            : base("Unknown error")
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class with the given message
        /// </summary>
        /// <param name="message">Message describing the exception</param>
        public CacheException(string message)
            : base(message)
        {
            Contract.Requires(message != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class with the given message.
        /// </summary>
        /// <param name="format">Message format describing the exception</param>
        /// <param name="arguments">Message format arguments</param>
        public CacheException(string format, params object[] arguments)
            : this(string.Format(CultureInfo.InvariantCulture, format, arguments))
        {
            Contract.Requires(format != null);
            Contract.Requires(arguments != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class with the given message and inner exception.
        /// </summary>
        /// <param name="message">Message describing the exception</param>
        /// <param name="innerException">Earlier, inner exception</param>
        public CacheException(string message, Exception innerException)
            : base(message, innerException)
        {
            Contract.Requires(message != null);
            Contract.Requires(innerException != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class with the given message and inner exception
        /// </summary>
        /// <param name="innerException">Earlier, inner exception</param>
        /// <param name="format">Message format describing the exception</param>
        /// <param name="arguments">Message format arguments</param>
        public CacheException(Exception innerException, string format, params object[] arguments)
            : this(string.Format(CultureInfo.InvariantCulture, format, arguments), innerException)
        {
            Contract.Requires(innerException != null);
            Contract.Requires(format != null);
            Contract.Requires(arguments != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CacheException" /> class based on the serialization info and streaming
        ///     context.
        /// </summary>
        /// <param name="info">Info that holds serialized data about exception</param>
        /// <param name="context">Context that contains information about source or destination</param>
        protected CacheException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Contract.Requires(info != null);
        }
    }
}
