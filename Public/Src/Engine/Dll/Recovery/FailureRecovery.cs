// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;

namespace BuildXL.Engine.Recovery
{
    /// <summary>
    /// Abstract class representing recovery action in the presence of previous build failure. 
    /// </summary>
    public abstract class FailureRecovery
    {
        /// <summary>
        /// Name to identify recovery action.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// BuildXL configuration.
        /// </summary>
        protected readonly IConfiguration Configuration;

        /// <summary>
        /// Path table.
        /// </summary>
        protected readonly PathTable PathTable;

        /// <summary>
        /// Creates an instance of <see cref="FailureRecovery"/>.
        /// </summary>
        protected FailureRecovery(string name, PathTable pathTable, IConfiguration configuration)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            Contract.Requires(pathTable != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(configuration != null);

            Name = name;
            PathTable = pathTable;
            Configuration = configuration;
        }

        /// <summary>
        /// Checks if failure should be marked.
        /// </summary>
        /// <param name="exception">Exception that causes the failure.</param>
        /// <param name="rootCause"><see cref="ExceptionRootCause"/></param>
        /// <returns>True iff failure should be marked.</returns>
        public abstract bool ShouldMarkFailure([NotNull] Exception exception, ExceptionRootCause rootCause);

        /// <summary>
        /// Marks for failure.
        /// </summary>
        /// <param name="exception">Exception that causes the failure.</param>
        /// <returns>Succeeds if marking was successful.</returns>
        public abstract Possible<Unit> MarkFailure([NotNull] Exception exception);

        /// <summary>
        /// Condition to determine whether the recovery action should be run.
        /// </summary>
        /// <returns>True iff the recovery action should be run.</returns>
        public abstract bool ShouldRecover();

        /// <summary>
        /// Runs recovery actions.
        /// </summary>
        /// <returns>Succeeds if recovery was successful.</returns>
        public abstract Possible<Unit> Recover();

        /// <summary>
        /// Tries to mark failure if the condition for marking failure (<see cref="ShouldMarkFailure(Exception, ExceptionRootCause)"/>) is satisfied.
        /// </summary>
        /// <param name="exception">Exception that causes the failure.</param>
        /// <param name="rootCause"><see cref="ExceptionRootCause"/></param>
        /// <returns>Succeeds if marking was successful.</returns>
        public Possible<Unit> TryMarkFailure([NotNull] Exception exception, ExceptionRootCause rootCause)
        {
            if (!ShouldMarkFailure(exception, rootCause))
            {
                return Unit.Void;
            }

            return MarkFailure(exception);
        }

        /// <summary>
        /// Tries to recover if the recovery condition (<see cref="ShouldRecover()"/>) is satisfied.
        /// </summary>
        /// <returns>Succeeds if recovery was successful.</returns>
        public Possible<Unit> TryRecover()
        {
            if (!ShouldRecover())
            {
                return Unit.Void;
            }

            return Recover();
        }
    }
}
