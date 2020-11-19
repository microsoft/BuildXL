// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// Represents an exception from the cache service.
    /// </summary>
    [Serializable]
    public class CacheServiceException : Exception
    {
        private const string CacheErrorReasonCodeName = "CacheErrorReasonCode";

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        public CacheServiceException()
        {
            ReasonCode = CacheErrorReasonCode.Unknown;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        public CacheServiceException(string message)
            : this(message, CacheErrorReasonCode.Unknown)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        public CacheServiceException(string message, Exception innerException)
            : this(message, innerException, CacheErrorReasonCode.Unknown)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        public CacheServiceException(string message, CacheErrorReasonCode reasonCode)
            : base(message)
        {
            ReasonCode = reasonCode;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        public CacheServiceException(string message, Exception innerException, CacheErrorReasonCode reasonCode)
            : base(message, innerException)
        {
            ReasonCode = reasonCode;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheServiceException"/> class.
        /// </summary>
        protected CacheServiceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ReasonCode = (CacheErrorReasonCode)info.GetValue(CacheErrorReasonCodeName, typeof(CacheErrorReasonCode));
        }

        /// <summary>
        /// Gets the reason for the cache service exception.
        /// </summary>
        public CacheErrorReasonCode ReasonCode { get; }

        /// <inheritdoc />
#if !NET_COREAPP_50
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            base.GetObjectData(info, context);
            info.AddValue(CacheErrorReasonCodeName, ReasonCode);
        }
    }
}
