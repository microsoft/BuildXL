// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// A ref-count reference for session's lifetime tracking purposes.
    /// </summary>
    public interface ISessionReference<out TSession> : IDisposable where TSession : IContentSession?
    {
        /// <nodoc />
        [NotNull]
        public TSession Session { get; }
    }

    /// <nodooc />
    internal sealed class SessionReference<TSession> : ISessionReference<TSession> where TSession : IContentSession?
    {
        private readonly ISessionLifetimeManager _lifetimeManager;

        /// <nodoc />
        public SessionReference([NotNull]TSession session, ISessionLifetimeManager lifetimeManager)
        {
            Contract.Requires(session is not null);
            Session = session;

            _lifetimeManager = lifetimeManager;
            _lifetimeManager.IncrementUsageCount();
        }

        /// <inheritdoc />
        [NotNull]
        public TSession Session { get; }

        /// <inheritdoc />
        public void Dispose() => _lifetimeManager.DecrementUsageCount();
    }

    /// <summary>
    /// A handler for sessions.
    /// </summary>
    public interface ISessionHandler<out TSession, in TSessionData> where TSession : IContentSession?
    {
        /// <summary>
        /// Gets the session by <paramref name="sessionId"/>.
        /// Returns null if the session is not found.
        /// </summary>
        ISessionReference<TSession>? GetSession(int sessionId);

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
        public static bool TryGetSession<TSession, TSessionData>(
            this ISessionHandler<TSession, TSessionData> sessionHandler,
            int sessionId,
            [NotNullWhen(true)]out ISessionReference<TSession>? sessionReference) where TSession : IContentSession?
        {
            sessionReference = sessionHandler.GetSession(sessionId);
            return sessionReference != null;
        }
    }
}
