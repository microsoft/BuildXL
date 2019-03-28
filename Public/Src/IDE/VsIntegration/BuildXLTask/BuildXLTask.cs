// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.AccessControl;
using System.Threading;
using BuildXL.Scheduler;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TplTask = System.Threading.Tasks.Task;

namespace BuildXL.IDE.BuildXLTask
{
    /// <summary>
    /// Creates an MSBuild task to launch BuildXL from Visual Studio.
    /// </summary>
    // ReSharper disable MemberCanBePrivate.Global
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed class BuildXLTask : Task, ICancelableTask
    {
        /// <summary>
        /// BuildXL Specification file
        /// </summary>
        public ITaskItem DominoSpecFile { get; set; }

        /// <summary>
        /// BuildXL value that needs to be resolved
        /// </summary>
        [Required]
        public string DominoValue { get; set; }

        /// <summary>
        /// The event name to wait on for the host object to finish launching BuildXL.
        /// </summary>
        [Required]
        public string DominoHostEventName { get; set; }

        /// <summary>
        /// The port to use to connect to the BuildXL IDE service
        /// </summary>
        [Required]
        public int Port { get; set; }

        /// <summary>
        /// Specifies the platform
        /// </summary>
        [Required]
        public string Platform { get; set; }

        /// <summary>
        /// Specifies the configuration
        /// </summary>
        [Required]
        public string Configuration { get; set; }

        private readonly CancellationTokenSource m_cancellationTokenSource = new CancellationTokenSource();

        private static readonly TimeSpan MaxProjectBuildTime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EventPollingFrequency = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Main method for launching BuildXL task
        /// </summary>
        /// <returns>Whether the task is executed successfully or not</returns>
        public override bool Execute()
        {
            if (Environment.GetEnvironmentVariable("DominoTaskDebugOnStart") == "1")
            {
                Debugger.Launch();
            }

            // Initializing logger
            var logger = new BuildXLTaskLogger(Log);

            logger.LogMessage(
                    string.Format(CultureInfo.InvariantCulture, "Building value {0} with qualifier {1}", DominoValue, Configuration));

            TplTask waitForDominoExitTask = null;
            try
            {
                EventWaitHandle hostEvent;
                if (!string.IsNullOrEmpty(DominoHostEventName) && EventWaitHandle.TryOpenExisting(DominoHostEventName, EventWaitHandleRights.Synchronize, out hostEvent))
                {
                    waitForDominoExitTask = TplTask.Run(() =>
                    {
                        using (hostEvent)
                        {
                            // The hostEvent will be triggered if the BuildXL process exits.
                            // The cancellation token will be triggered if the task is cancelled or completes
                            WaitHandle.WaitAny(new[] { m_cancellationTokenSource.Token.WaitHandle, hostEvent });
                            if (!m_cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                logger.LogError("BuildXL process exited before completing task.");
                                m_cancellationTokenSource.Cancel();
                            }
                        }
                    });
                }
                else
                {
                    logger.LogError(Strings.HostEventNotFound, DominoHostEventName);
                    return false;
                }

                try
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    using (EventWaitHandle success = new EventWaitHandle(false, EventResetMode.ManualReset, DominoValue.ToUpperInvariant() + Port + Scheduler.Scheduler.IdeSuccessPrefix))
                    using (EventWaitHandle failure = new EventWaitHandle(false, EventResetMode.ManualReset, DominoValue.ToUpperInvariant() + Port + Scheduler.Scheduler.IdeFailurePrefix))
                    {
                        while (timer.Elapsed < MaxProjectBuildTime)
                        {
                            if (success.WaitOne(EventPollingFrequency))
                            {
                                break;
                            }

                            if (failure.WaitOne(EventPollingFrequency))
                            {
                                logger.LogError(string.Format(CultureInfo.InvariantCulture, Strings.FailureMessage, DominoValue));
                                return false;
                            }

                            if (m_cancellationTokenSource.IsCancellationRequested)
                            {
                                return false;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError("Failed to execute BuildXL");
                    logger.LogErrorFromException(exception);
                    return false;
                }

                logger.LogMessage(string.Format(CultureInfo.InvariantCulture, Strings.SuccessMessage, DominoValue));
                return true;
            }
            finally
            {
                m_cancellationTokenSource.Cancel();
                if (waitForDominoExitTask != null)
                {
                    waitForDominoExitTask.Wait();
                }
            }
        }

        /// <summary>
        /// Cancel the task
        /// </summary>
        public void Cancel()
        {
            m_cancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    /// This class is responsible for logging and also asynchronously gather BuildXL's standard output and redirect it to
    /// MSBuild tasks's output
    /// </summary>
    internal sealed class BuildXLTaskLogger
    {
        private const string Prefix = "BuildXL: ";

        /// <summary>
        /// Stores the helper class of MSBuild task. This is used to output messages when the task is executed from console
        /// </summary>
        private readonly TaskLoggingHelper m_logHelper;

        /// <summary>
        /// Constructor for initializing the custom logger
        /// </summary>
        /// <param name="logHelper">The helper instance for writing the messages</param>
        public BuildXLTaskLogger(TaskLoggingHelper logHelper)
        {
            m_logHelper = logHelper;
        }

        /// <summary>
        /// Outputs messages based on whether the task is initiated within visual studio IDE or not
        /// </summary>
        internal void LogMessage(string message, params object[] args)
        {
            m_logHelper.LogMessage(MessageImportance.High, Prefix + message, args);
        }

        /// <summary>
        /// Outputs messages based on whether the task is initiated within visual studio IDE or not
        /// </summary>
        internal void LogError(string error, params object[] args)
        {
            m_logHelper.LogError(Prefix + error, args);
        }

        /// <summary>
        /// Logs error from exception
        /// </summary>
        /// <param name="exception">Exception to be logged</param>
        internal void LogErrorFromException(Exception exception)
        {
            m_logHelper.LogMessage(MessageImportance.High, exception.GetType().FullName + ":\n" + exception.Message + "\n" + exception.StackTrace);

            AggregateException aggregateException = exception as AggregateException;
            if (aggregateException != null)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    LogErrorFromException(innerException);
                }
            }
        }
    }
}
