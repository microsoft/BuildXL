// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Wrapper around System.Threading.Semaphore.
    /// </summary>
    public class WindowsNamedSemaphore : INamedSemaphore
    {
        /// <inheritdoc/>
        public string Name => m_name;

        private readonly System.Threading.Semaphore m_semaphore;
        private readonly string m_name;
        private bool m_disposed;

        /// <nodoc/>
        private WindowsNamedSemaphore(string name, System.Threading.Semaphore semaphore)
        {
            m_name = name;
            m_semaphore = semaphore;
        }

        /// <nodoc/>
        ~WindowsNamedSemaphore() => Dispose(false);

        /// <summary>
        /// Try to create a named System.Threading.Semaphore.
        /// </summary>
        /// <param name="name">Name of the semaphore.</param>
        /// <param name="initialCount">Initial count of the semaphore</param>
        /// <param name="maximumCount">Maximum count of the semaphore</param>
        public static Possible<INamedSemaphore> CreateNew(string name, int initialCount, int maximumCount)
        {
            try
            {
                var sem = new System.Threading.Semaphore(initialCount, maximumCount, name, out var newlyCreated);
                if (!newlyCreated)
                {
                    return new Failure<string>($"Failed to create semaphore with name '{name}' and value {initialCount} because a semaphore with this name already exists.");
                }

                return new WindowsNamedSemaphore(name, sem);
            }
            catch (Exception e)
            {
                return new Failure<string>(e.ToString());
            }
        }

        /// <inheritdoc />
        public int Release() => m_semaphore.Release();

        /// <inheritdoc />
        public bool WaitOne(int timeoutMilliseconds) => m_semaphore.WaitOne(timeoutMilliseconds);

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <nodoc/>
        public void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_semaphore.Dispose();
                }

                m_disposed = true;
            }
        }
    }
}
