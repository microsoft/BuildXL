// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Tracing;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Class to track the state of warning messages for a command-line tool.
    /// </summary>
    /// <remarks>
    /// The point of this class is to build a repository of configured warnings and then
    /// be able to answer the question "should this message be treated like a warning,
    /// like an error, or should it be suppressed?"
    /// </remarks>
    public sealed class WarningManager
    {
        private readonly Dictionary<int, WarningState> m_warningStates = new Dictionary<int, WarningState>();
        private bool m_allWarningsAreErrors;

        /// <summary>
        /// Controls the state of a particular warning.
        /// </summary>
        public void SetState(int warningNumber, WarningState state)
        {
            Contract.Requires(warningNumber >= 0);

            m_warningStates[warningNumber] = state;
        }

        /// <summary>
        /// Returns the expected disposition for a particular warning message.
        /// </summary>
        public WarningState GetState(int warningNumber)
        {
            Contract.Requires(warningNumber >= 0);

            WarningState result;

            if (m_warningStates.TryGetValue(warningNumber, out result))
            {
                if (result == WarningState.Suppressed)
                {
                    return WarningState.Suppressed;
                }
            }
            else
            {
                // default to warning if not known
                result = WarningState.AsWarning;
            }

            if (m_allWarningsAreErrors)
            {
                // global override
                return WarningState.AsError;
            }

            // all done
            return result;
        }

        /// <summary>
        /// Controls whether all warnings are treated as errors, regardless of the individual per-warning setting.
        /// </summary>
        public bool AllWarningsAreErrors
        {
            get { return m_allWarningsAreErrors; }
            set { m_allWarningsAreErrors = value; }
        }
    }
}
