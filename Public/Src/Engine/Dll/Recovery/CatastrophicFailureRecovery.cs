// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;

namespace BuildXL.Engine.Recovery
{
    /// <summary>
    /// Handling for unknown exceptions thrown during the build, AKA catastrophic failures.
    /// </summary>
    public class CatastrophicFailureRecovery : FailureRecovery
    {
        private readonly LoggingContext m_loggingContext;

        /// <inheritdoc />
        public CatastrophicFailureRecovery(PathTable pathTable, IConfiguration configuration, LoggingContext loggingContext)
            : base(nameof(CatastrophicFailureRecovery), pathTable, configuration)
        {
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Pre-emptively attempts to handle failures of <see cref="ExceptionRootCause.Unknown"/> by logging and removing
        /// the engine state that is persisted build-over-build.
        /// </summary>
        /// <remarks>
        /// This is done at the end of the build during <see cref="MarkFailure(Exception)"/>, since there is no reason
        /// to defer it to <see cref="Recover"/> which runs at the beginning of the next build.
        /// </remarks>
        public override Possible<Unit> MarkFailure([NotNull] Exception exception)
        {
            Logger.Log.LogAndRemoveEngineStateOnCatastrophicFailure(m_loggingContext);
            if (EngineSerializer.TryLogAndRemoveCorruptEngineState(Configuration, PathTable, m_loggingContext))
            {
                return Unit.Void;
            }

            return new Failure<string>("Error logging and removing engine state while handling catastrophic failure.");
        }

        /// <summary>
        /// No-op, <see cref="MarkFailure(Exception)"/> for recovery handling information.
        /// </summary>
        public override Possible<Unit> Recover()
        {
            return Unit.Void;
        }

        /// <summary>
        /// Only run handling for failures of unknown root cause. Many of the known root causes are unrelated to the build
        /// and cannot be handled during the build.
        /// </summary>
        public override bool ShouldMarkFailure([NotNull] Exception exception, ExceptionRootCause rootCause)
        {
            return rootCause == ExceptionRootCause.Unknown;
        }

        /// <summary>
        /// No-op, <see cref="MarkFailure(Exception)"/> for recovery handling information.
        /// </summary>
        public override bool ShouldRecover()
        {
            return false;
        }
    }
}
