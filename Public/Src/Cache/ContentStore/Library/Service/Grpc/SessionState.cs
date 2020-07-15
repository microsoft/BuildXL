// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Holds the client-side session state, which is just the session id and associated temporary directory,
    /// and re-generates it, if required, in a thread-safe way, ensuring that only one new session id is generated after each reset.
    /// </summary>
    public class SessionState : IDisposable
    {
        private static readonly Tracer Tracer = new Tracer(nameof(SessionState));

        // Tracking original id of the instance for tracing purposes because SessionData can be null.
        private readonly int _originalId;

        private SessionData? _data;

        private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);

        private readonly Func<Task<Result<SessionData>>> _sessionFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionState"/> class.
        /// </summary>
        /// <param name="sessionFactory">A factory method that will be called to create new sessions.</param>
        /// <param name="data">The initial session data.</param>
        public SessionState(Func<Task<Result<SessionData>>> sessionFactory, SessionData data)
        {
            _data = data;
            _originalId = _data.SessionId;
            _sessionFactory = sessionFactory;
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
        /// <param name="context">A context of the operation.</param>
        /// <param name="badId">The bad session ID.</param>
        public Task ResetAsync(OperationContext context, int badId)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    double lockAcquisitionDurationMs = 0;
                    // To make sure a competing thread doesn't squash a newly created session Id,
                    // only squash the session ID if we are the first to discover that ours is bad.
                    using (var releaser = await _sync.AcquireAsync())
                    {
                        if (_data != null && _data.SessionId == badId)
                        {
                            _data.TemporaryDirectory?.Dispose();
                            _data = null;
                        }

                        lockAcquisitionDurationMs = releaser.LockAcquisitionDuration.TotalMilliseconds;
                    }

                    return Result.Success(lockAcquisitionDurationMs);
                },
                extraEndMessage: r => $"LockAcquisitionDuration=[{r.ToStringWithValue()}ms]");
        }

        /// <summary>
        /// Gets the current active session data.
        /// </summary>
        public async Task<Result<SessionData>> GetDataAsync(OperationContext context)
        {
            // Use double-checked locking to ensure only one session is created,
            // in most circumstances without blocking.
            SessionData? data = _data;
            if (data == null)
            {
                using (var releaser = await _sync.AcquireAsync())
                {
                    if (_data == null)
                    {
                        Result<SessionData> result = await context.PerformOperationAsync(
                            Tracer,
                            () => _sessionFactory(),
                            extraStartMessage: $"OriginalId=[{_originalId}]",
                            extraEndMessage: r => $"OriginalId=[{_originalId}], LockAcquisitionDuration=[{releaser.LockAcquisitionDuration.TotalMilliseconds}ms]. Result={r.ToStringWithValue()}");

                        if (!result)
                        {
                            return result;
                        }
                        else
                        {
                            _data = result.Value;
                        }
                    }
                }

                Contract.Assert(_data != null);
                return new Result<SessionData>(_data);
            }
            else
            {
                return new Result<SessionData>(data);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            SessionData? data = _data;
            return $"SessionId=[{data?.SessionId.ToString() ?? "Unknown"}], OriginalId=[{_originalId}]";
        }
    }
}
