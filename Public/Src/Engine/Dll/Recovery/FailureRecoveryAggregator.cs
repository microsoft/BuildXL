// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using System;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Engine.Recovery
{
    /// <summary>
    /// Class for recovering from (unhandled) failures.
    /// </summary>
    public class FailureRecoveryAggregator
    {
        private readonly FailureRecovery[] m_actions;
        private readonly LoggingContext m_loggingContext;

        private FailureRecoveryAggregator(LoggingContext loggingContext, FailureRecovery[] actions)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(actions != null);

            m_loggingContext = loggingContext;
            m_actions = actions;
        }

        /// <summary>
        /// Creates an instance of <see cref="FailureRecoveryAggregator"/>.
        /// </summary>
        /// <returns>An instance of <see cref="FailureRecoveryAggregator"/> if successful; otherwise null.</returns>
        public static FailureRecoveryAggregator Create(LoggingContext loggingContext, FailureRecovery[] actions)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(actions != null);
            Contract.Requires(actions.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() == actions.Length, "Action names must be unique");

            return new FailureRecoveryAggregator(loggingContext, actions);
        }

        /// <summary>
        /// Tries to recover from failures.
        /// </summary>
        /// <returns>True if recovery is successful.</returns>
        /// <remarks>If one of recovery action failed, then an error was logged.</remarks>
        public bool TryRecoverIfNeeded(bool stopOnFirstFailure = true)
        {
            bool success = true;

            foreach (var action in m_actions)
            {
                if (action.ShouldRecover())
                {
                    var possibleResult = action.TryRecover();

                    if (!possibleResult.Succeeded)
                    {
                        success = false;
                        Logger.Log.FailedToRecoverFailure(
                            m_loggingContext,
                            action.Name, 
                            possibleResult.Failure.DescribeIncludingInnerFailures());

                        if (stopOnFirstFailure)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        Logger.Log.SuccessfulFailureRecovery(m_loggingContext, action.Name);
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Marks for failure.
        /// </summary>
        /// <param name="exception">Exception that causes the failure.</param>
        /// <param name="rootCause"><see cref="ExceptionRootCause"/></param>
        /// <returns>True if all markings were successful.</returns>
        public bool TryMarkFailure(Exception exception, ExceptionRootCause rootCause)
        {
            bool success = true;

            foreach (var action in m_actions)
            {
                if (action.ShouldMarkFailure(exception, rootCause))
                {
                    var possibleResult = action.TryMarkFailure(exception, rootCause);

                    if (!possibleResult.Succeeded)
                    {
                        Logger.Log.FailedToMarkFailure(
                            m_loggingContext,
                            action.Name,
                            possibleResult.Failure.DescribeIncludingInnerFailures());
                        success = false;
                    }
                    else
                    {
                        Logger.Log.SuccessfulMarkFailure(m_loggingContext, action.Name);
                    }
                }
            }

            return success;
        }
    }
}
