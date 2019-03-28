// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Stores session data, which consists of a session ID and associated temporary directory.
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// Gets or sets the session ID.
        /// </summary>
        public int SessionId { get; set; }

        /// <summary>
        /// Gets or sets the assoicated temporary directory.
        /// </summary>
        public DisposableDirectory TemporaryDirectory { get; set; }
    }
}
