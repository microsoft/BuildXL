// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;
using System.Threading;
using BuildXL.Processes;
using BuildXL.Utilities;

namespace BuildXL.SandboxedProcessExecutor
{
    /// <summary>
    /// Observer of standard output and standard error.
    /// </summary>
    internal class OutputErrorObserver
    {
        private readonly Regex m_warningRegex;
        private readonly bool m_isDefaultWarningRegex;

        private readonly bool m_logOutputToConsole;
        private readonly bool m_logErrorToConsole;

        private int m_warningCount;

        private readonly ConsoleLogger m_logger;

        /// <summary>
        /// Total number of warnings.
        /// </summary>
        public int WarningCount => Volatile.Read(ref m_warningCount);

        private OutputErrorObserver(ConsoleLogger logger, Regex warningRegex, bool isDefaultWarningRegex, bool logOutputToConsole, bool logErrorToConsole)
        {
            m_logger = logger;
            m_warningRegex = warningRegex;
            m_isDefaultWarningRegex = isDefaultWarningRegex;
            m_logOutputToConsole = logOutputToConsole;
            m_logErrorToConsole = logErrorToConsole;
        }

        private bool IsWarning(string line)
        {
            Contract.Requires(line != null);

            if (m_warningRegex == null)
            {
                return false;
            }

            // An unusually long string causes pathologically slow Regex back-tracking.
            // To avoid that, only scan the first 400 characters. That's enough for
            // the longest possible prefix: MAX_PATH, plus a huge subcategory string, and an error location.
            // After the regex is done, we can append the overflow.
            if (line.Length > 400)
            {
                line = line.Substring(0, 400);
            }

            // If a tool has a large amount of output that isn't a warning (eg., "dir /s %hugetree%")
            // the regex matching below may be slow. It's faster to pre-scan for "warning"
            // and bail out if neither are present.
            if (m_isDefaultWarningRegex &&
                line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return false;
            }

            return m_warningRegex.IsMatch(line);
        }

        /// <summary>
        /// Creates an instance of <see cref="OutputErrorObserver"/>
        /// </summary>
        public static OutputErrorObserver Create(ConsoleLogger logger, SandboxedProcessInfo info)
        {
            Contract.Requires(info != null);

            Regex warningRegex = info.StandardObserverDescriptor == null || info.StandardObserverDescriptor.WarningRegex == null
                ? null
                : CreateRegex(info.StandardObserverDescriptor.WarningRegex);
            bool isDefaultWarningRegex = warningRegex == null ? false : IsDefaultWarningRegex(info.StandardObserverDescriptor.WarningRegex);
            bool logOutputToConsole = info.StandardObserverDescriptor == null ? false : info.StandardObserverDescriptor.LogOutputToConsole;
            bool logErrorToConsole = info.StandardObserverDescriptor == null ? false : info.StandardObserverDescriptor.LogErrorToConsole;

            return new OutputErrorObserver(logger, warningRegex, isDefaultWarningRegex, logOutputToConsole, logErrorToConsole);
        }

        private static bool IsDefaultWarningRegex(ExpandedRegexDescriptor regexDescriptor) 
            => string.Equals(regexDescriptor.Pattern, Warning.DefaultWarningPattern) && regexDescriptor.Options == RegexOptions.IgnoreCase;

        private static Regex CreateRegex(ExpandedRegexDescriptor regexDescriptor) 
            => new Regex(
                    regexDescriptor.Pattern,
                    regexDescriptor.Options | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Observes standard output for warning.
        /// </summary>
        public void ObserveStandardOutputForWarning(string outputLine)
        {
            if (IsWarning(outputLine))
            {
                Interlocked.Increment(ref m_warningCount);
            }

            if (m_logOutputToConsole)
            {
                m_logger.LogInfo(outputLine);
            }
        }

        /// <summary>
        /// Observes standard error for warning.
        /// </summary>
        public void ObserveStandardErrorForWarning(string errorLine)
        {
            if (IsWarning(errorLine))
            {
                Interlocked.Increment(ref m_warningCount);
            }

            if (m_logErrorToConsole)
            {
                m_logger.LogError(errorLine);
            }
        }
    }
}
