// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public int SessionId { get; }

        /// <summary>
        /// Gets or sets the associated temporary directory.
        /// </summary>
        public DisposableDirectory TemporaryDirectory { get; }

        /// <nodoc />
        public SessionData(int sessionId, DisposableDirectory temporaryDirectory)
            => (SessionId, TemporaryDirectory) = (sessionId, temporaryDirectory);
    }
}
