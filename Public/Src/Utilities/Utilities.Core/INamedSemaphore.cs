// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Abstraction for a cross-platform semaphore using pthreads on Linux or System.Threading.Semaphore on Windows.
    /// </summary>
    public interface INamedSemaphore : IDisposable
    {
        /// <summary>
        /// Name of this semaphore
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Increment the semaphore by one.
        /// </summary>
        /// <returns>The value of the semaphore before it was incremented.</returns>
        public int Release();

        /// <summary>
        /// Decrement the semaphore by one.
        /// </summary>
        /// <param name="timeoutMilliseconds">If less than zero, then a blocking wait will be used.</param>
        /// <returns>False if a timed out.</returns>
        public bool WaitOne(int timeoutMilliseconds);
    }

    /// <summary>
    /// Factory class to create cross-platform semaphores.
    /// </summary>
    public class SemaphoreFactory
    {
        /// <summary>
        /// Creates a semaphore based on the current host platform.
        /// </summary>
        /// <param name="name">Name for the named semaphore.</param>
        /// <param name="initialCount">Initial value of the semaphore.</param>
        /// <param name="maximumCount">Maximum value of the semaphore (ignored for POSIX semaphores).</param>
        /// <returns>Semaphore based on platform if successful, else null.</returns>
        /// <exception cref="Exception">Not supported on non-Windows/non-Linux platforms.</exception>
        public static Possible<INamedSemaphore> CreateNew(string name, int initialCount, int maximumCount)
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return WindowsNamedSemaphore.CreateNew(name, initialCount, maximumCount);
            }
            else if (OperatingSystemHelper.IsLinuxOS)
            {
                return LinuxNamedSemaphore.CreateNew(name, (uint)initialCount);
            }
            else
            {
                return new Failure<PlatformNotSupportedException>(new PlatformNotSupportedException($"Named Semaphores are not supported on current OS."));
            }
        }
    }
}
