// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Processes
{
    /// <summary>
    /// Timing information
    /// </summary>
    /// <remarks>
    /// This information is obtained via the Win32 GetProcessTimes function.
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms683223(v=vs.85).aspx
    /// </remarks>
    public sealed class ProcessTimes
    {
        #region constant copied from internal BCL sources in order to check preconditions

        // Number of 100ns ticks per time unit
        private const long TicksPerMillisecond = 10000;
        private const long TicksPerSecond = TicksPerMillisecond * 1000;
        private const long TicksPerMinute = TicksPerSecond * 60;
        private const long TicksPerHour = TicksPerMinute * 60;
        private const long TicksPerDay = TicksPerHour * 24;

        // Number of days in a non-leap year
        private const int DaysPerYear = 365;

        // Number of days in 4 years
        private const int DaysPer4Years = (DaysPerYear * 4) + 1;

        // Number of days in 100 years
        private const int DaysPer100Years = (DaysPer4Years * 25) - 1;

        // Number of days in 400 years
        private const int DaysPer400Years = (DaysPer100Years * 4) + 1;

        // Number of days from 1/1/0001 to 12/31/1600
        private const int DaysTo1601 = DaysPer400Years * 4;

        // Number of days from 1/1/0001 to 12/31/9999
        private const int DaysTo10000 = (DaysPer400Years * 25) - 366;

        private const long FileTimeOffset = DaysTo1601 * TicksPerDay;
        private const long MaxTicks = (DaysTo10000 * TicksPerDay) - 1;

        #endregion

        private readonly long m_create;
        private readonly long m_exit;
        private readonly long m_kernel;
        private readonly long m_user;

        /// <summary>
        /// Creates an instance
        /// </summary>
        public ProcessTimes(long creation, long exit, long kernel, long user)
        {
            Contract.Requires(creation >= 0);
            Contract.Requires(creation < MaxTicks - FileTimeOffset);

            // 'exit' may be undefined if process is still running
            Contract.Requires(kernel >= 0);
            Contract.Requires(user >= 0);
            m_create = creation;
            m_exit = exit;
            m_kernel = kernel;
            m_user = user;
        }

        /// <summary>
        /// Time when the process started
        /// </summary>
        public DateTime StartTimeUtc => DateTime.FromFileTimeUtc(m_create);

        /// <summary>
        /// Time when the process finished
        /// </summary>
        /// <remarks>
        /// If the process has not finished yet, this value is undefined.
        /// </remarks>
        public DateTime ExitTimeUtc
        {
            get
            {
                try
                {
                    return DateTime.FromFileTimeUtc(m_exit);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // dealing with possibly undefined m_exit value
                    return DateTime.MaxValue;
                }
            }
        }

        /// <summary>
        /// Time spent in the kernel
        /// </summary>
        /// <remarks>
        /// If multiple cores are present, this time can exceed the amount of time elapsed between StartTime and ExitTime.
        /// </remarks>
        public TimeSpan PrivilegedProcessorTime => new TimeSpan(m_kernel);

        /// <summary>
        /// Time spent in user land
        /// </summary>
        /// <remarks>
        /// If multiple cores are present, this time can exceed the amount of time elapsed between StartTime and ExitTime.
        /// </remarks>
        public TimeSpan UserProcessorTime => new TimeSpan(m_user);

        /// <summary>
        /// Total time spent in kernel and user land
        /// </summary>
        /// <remarks>
        /// If multiple cores are present, this time can exceed the amount of time elapsed between StartTime and ExitTime.
        /// </remarks>
        public TimeSpan TotalProcessorTime => new TimeSpan(m_user + m_kernel);

        /// <summary>
        /// Total time between creation and exit.
        /// </summary>
        public TimeSpan TotalWallClockTime => StartTimeUtc < ExitTimeUtc ? ExitTimeUtc - StartTimeUtc : TimeSpan.Zero;
    }
}
