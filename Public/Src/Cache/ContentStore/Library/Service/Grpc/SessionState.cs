// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Holds the client-side session state, which is just the session id and associated temporary directory,
    /// and re-generates it, if required, in a thread-safe way, ensuring that only one new session id is generated after each reset.
    /// </summary>
    public class SessionState : IDisposable
    {
        private SessionData _data;

        private SemaphoreSlim _sync = new SemaphoreSlim(1, 1);

        private Func<Task<ObjectResult<SessionData>>> _sessionFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionState"/> class.
        /// </summary>
        /// <param name="sessionFactory">A factory method that will be called to create new sessions.</param>
        public SessionState(Func<Task<ObjectResult<SessionData>>> sessionFactory)
        {
            _sessionFactory = sessionFactory;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionState"/> class.
        /// </summary>
        /// <param name="sessionFactory">A factory method that will be called to create new sessions.</param>
        /// <param name="data">The initial session data.</param>
        public SessionState(Func<Task<ObjectResult<SessionData>>> sessionFactory, SessionData data)
            : this(sessionFactory)
        {
            _data = data;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _data?.TemporaryDirectory?.Dispose();
            _sync.Dispose();
        }

        /// <summary>
        /// Informs the session state that the given session ID is bad and should not be used in future.
        /// </summary>
        /// <param name="badId">The bad session ID.</param>
        public async Task ResetAsync(int badId)
        {
            // To make sure a competing thread doesn't squash a newly created session Id,
            // only squash the session ID if we are the first to discover that ours is bad.
            await _sync.WaitAsync();
            try
            {
                if ((_data != null) && (_data.SessionId == badId))
                {
                    _data.TemporaryDirectory?.Dispose();
                    _data = null;
                }
            }
            finally
            {
                _sync.Release();
            }
        }

        /// <summary>
        /// Gets the currently active session ID.
        /// </summary>
        public async Task<StructResult<int>> GetIdAsync()
        {
            ObjectResult<SessionData> result = await GetDataAsync();
            if (result.Succeeded)
            {
                return new StructResult<int>(result.Data.SessionId);
            }
            else
            {
                return new StructResult<int>(result);
            }
        }

        /// <summary>
        /// Gets the current active session data.
        /// </summary>
        public async Task<ObjectResult<SessionData>> GetDataAsync()
        {
            // Use double-checked locking to ensure only one session is created,
            // in most circumstances without blocking.
            SessionData data = _data;
            if (data == null)
            {
                await _sync.WaitAsync();
                try
                {
                    if (_data == null)
                    {
                        ObjectResult<SessionData> result = await _sessionFactory();
                        if (!result.Succeeded)
                        {
                            return result;
                        }
                        else
                        {
                            _data = result.Data;
                        }
                    }

                    return new ObjectResult<SessionData>(_data);
                }
                finally
                {
                    _sync.Release();
                }
            }
            else
            {
                return new ObjectResult<SessionData>(data);
            }
        }
    }
}
