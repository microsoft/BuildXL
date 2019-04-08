// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.VstsInterfaces
{
    /// <summary>
    /// Represents the content store interface contract for the Artifact NotNullUri which has Expiry.
    /// </summary>
    public class ExpirableUri
    {
        /// <summary>
        /// The Uri that is expirable.
        /// </summary>
        public readonly Uri NotNullUri;

        /// <summary>
        /// When the URI expires.
        /// </summary>
        public readonly DateTime? MaybeExpiry;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpirableUri"/> class.
        /// </summary>
        public ExpirableUri(Uri notNullUri, DateTime? maybeExpiry = null)
        {
            Contract.Requires(notNullUri != null);
            MaybeExpiry = maybeExpiry;
            NotNullUri = notNullUri;
        }
    }
}
