// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// A handler for sessions.
    /// </summary>
    public interface ISessionHandler<out TSession, TSessionData> where TSession : IContentSession?
    {
        /// <summary>
        /// Gets the session by <paramref name="sessionId"/>.
        /// Returns null if the session is not found.
        /// </summary>
        [return: MaybeNull] TSession GetSession(int sessionId);

        /// <summary>
        /// Releases the session with the specified session id
        /// </summary>
        Task ReleaseSessionAsync(OperationContext context, int sessionId);

        /// <summary>
        /// Creates a session with the parameter specified.
        /// </summary>
        Task<Result<(int sessionId, AbsolutePath? tempDirectory)>> CreateSessionAsync(
            OperationContext context,
            TSessionData sessionData,
            string cacheName);

        /// <summary>
        /// Gets a current stats snapshot.
        /// </summary>
        Task<Result<CounterSet>> GetStatsAsync(OperationContext context);

        /// <summary>
        /// Removes local content locations and returns the number of hashes removed.
        /// </summary>
        Task<Result<long>> RemoveFromTrackerAsync(OperationContext context);
    }

    /// <nodoc />
    public static class SessionHandlerExtensions
    {
        /// <nodoc />
        public static bool TryGetSession<TSession, TSessionData>(this ISessionHandler<TSession, TSessionData> sessionHandler, int sessionId, [MaybeNull][NotNullWhen(true)]out TSession session) where TSession : IContentSession?
        {
            session = sessionHandler.GetSession(sessionId);
            return session != null;
        }
    }
}
