// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    /// Encapsulates the result of a lock acquisition.
    /// </summary>
    public readonly struct LockAcquisitionResult
    {
        /// <summary>
        /// True if the lock was acquired, false otherwise.
        /// </summary>
        public bool LockAcquired { get; }

        /// <summary>
        /// If the lock acquisition failed, the property contains a time span that was spent trying to acquire the lock.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Optional competing processes ID that has held the lock.
        /// </summary>
        public int? CompetingProcessId { get; }

        /// <summary>
        /// Optional competing process name that has held the lock.
        /// </summary>
        public string CompetingProcessName { get; }

        /// <summary>
        /// Optional exception that caused the operation to fail.
        /// </summary>
        public Exception Exception { get; }

        /// <nodoc />
        private LockAcquisitionResult(bool lockAcquired, TimeSpan timeout, int? competingProcessId, string competingProcessName, Exception exception)
        {
            LockAcquired = lockAcquired;
            Timeout = timeout;
            CompetingProcessId = competingProcessId;
            CompetingProcessName = competingProcessName;
            Exception = exception;
        }

        /// <nodoc />
        public static LockAcquisitionResult Acquired() => new LockAcquisitionResult(true, TimeSpan.MinValue, competingProcessId: null, competingProcessName: null, exception: null);

        /// <nodoc />
        public static LockAcquisitionResult Failed(TimeSpan timeout, int? competingProcessId = null, string competingProcessName = null, Exception exception = null) =>
            new LockAcquisitionResult(false, timeout, competingProcessId, competingProcessName, exception);

        /// <summary>
        /// Returns an error message describing lock acquisition failure. Returns an empty string if the lock was successfully acquired.
        /// </summary>
        public string GetErrorMessage(string component)
        {
            System.Diagnostics.ContractsLight.Contract.Requires(!string.IsNullOrEmpty(component));

            if (LockAcquired)
            {
                return string.Empty;
            }

            string competingProcessId = CompetingProcessId == null
                ? string.Empty
                : System.Diagnostics.Process.GetCurrentProcess().Id != CompetingProcessId
                    ? $" The lock file is locked by another process (name: '{CompetingProcessName}', PID: '{CompetingProcessId}')."
                      // this should never happen since we never attempt to acquire a lock for the same lock file multiple times
                    : " The current process already holds a lock on the lock file.";

            string exceptionString = Exception == null
                ? string.Empty
                : $" Exception: {Exception}";

            return $"Failed to acquire single instance lock for {component} by {Timeout.TotalSeconds} seconds.{competingProcessId}.{exceptionString}";
        }
    }
}
