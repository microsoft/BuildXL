// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Common;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
#pragma warning disable SA1649 // File name must match first type name
#pragma warning disable SA1402 // File may only contain a single class
    /// <summary>
    /// Represents a cache miss from the build cache service.
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class ContentBagNotFoundException : VssServiceException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBagNotFoundException"/> class.
        /// </summary>
        public ContentBagNotFoundException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBagNotFoundException"/> class.
        /// </summary>
        public ContentBagNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBagNotFoundException"/> class.
        /// </summary>
        public ContentBagNotFoundException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBagNotFoundException"/> class.
        /// </summary>
        protected ContentBagNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// An exception for when an incorporate request fails on the build cache service.
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class StrongFingerprintIncorporateFailureException : VssServiceException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrongFingerprintIncorporateFailureException"/> class.
        /// </summary>
        public StrongFingerprintIncorporateFailureException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrongFingerprintIncorporateFailureException"/> class.
        /// </summary>
        public StrongFingerprintIncorporateFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrongFingerprintIncorporateFailureException"/> class.
        /// </summary>
        public StrongFingerprintIncorporateFailureException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StrongFingerprintIncorporateFailureException"/> class.
        /// </summary>
        protected StrongFingerprintIncorporateFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Exception thrown when the build cache service has missing blobs.
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2240:ImplementISerializableCorrectly")]
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [Serializable]
    public class MissingFinalizedBlobException : VssServiceException
    {
        /// <summary>
        /// Gets the missing blobs.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IEnumerable<BlobIdentifier> MissingBlobs { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MissingFinalizedBlobException"/> class.
        /// </summary>
        public MissingFinalizedBlobException(IEnumerable<BlobIdentifier> blobs)
        {
            MissingBlobs = blobs;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MissingFinalizedBlobException"/> class.
        /// </summary>
        public MissingFinalizedBlobException(IEnumerable<BlobIdentifier> blobs, string message, Exception innerException)
            : base(message, innerException)
        {
            MissingBlobs = blobs;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MissingFinalizedBlobException"/> class.
        /// </summary>
        public MissingFinalizedBlobException(IEnumerable<BlobIdentifier> blobs, string message)
            : base(message)
        {
            MissingBlobs = blobs;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MissingFinalizedBlobException"/> class.
        /// </summary>
        protected MissingFinalizedBlobException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
