// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Interop.Linux;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Wrapper for Linux pthread semaphores.
    /// </summary>
    public class LinuxNamedSemaphore : INamedSemaphore
    {
        /// <inheritdoc/>
        public string Name => m_name;

        private readonly IntPtr m_semaphore;
        private readonly string m_name;
        private bool m_disposed;

        /// <nodoc/>
        private LinuxNamedSemaphore(string name, IntPtr semaphore)
        {
            m_name = name;
            m_semaphore = semaphore;
        }

        /// <nodoc/>
        ~LinuxNamedSemaphore()
        {
            Dispose(false);
        }

        /// <summary>
        /// Try to create a named pthread semaphore.
        /// </summary>
        /// <param name="name">Name must be of the form /name up to 251 characters.</param>
        /// <param name="initialValue">Initial value of the semaphore</param>
        public static Possible<INamedSemaphore> CreateNew(string name, uint initialValue)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || name[0] != '/')
                {
                    return new Failure<ArgumentException>(new ArgumentException("Semaphore name must start with '/'"));
                }

                if (name.Count(c => c == '/') != 1)
                {
                    return new Failure<ArgumentException>(new ArgumentException("Semaphore name must start with '/' and contain exactly one '/' character"));
                }

                if (name.Length >= Ipc.SemaphoreNameMaxLength)
                {
                    return new Failure<ArgumentException>(new ArgumentException($"Semaphore name can only contain up to {Ipc.SemaphoreNameMaxLength} characters."));
                }

                var error = Ipc.SemOpen(name, initialValue, out var semaphore);

                if (semaphore == IntPtr.Zero || error != 0)
                {
                    return new Failure<string>($"Failed to create a semaphore with name '{name}' and value {initialValue} with errno: {error}");
                }

                return new LinuxNamedSemaphore(name, semaphore);
            }
            catch (Exception e)
            {
                return new Failure<Exception>(e);
            }
        }

        /// <inheritdoc />
        public int Release()
        {
            int previousValue = GetValue();
            int ret = Ipc.SemPost(m_semaphore);
            CheckReturnValue(ret);

            return previousValue;
        }

        /// <summary>
        /// Gets the current value of the semaphore
        /// </summary>
        private int GetValue()
        {
            int ret = Ipc.SemGetValue(m_semaphore, out int value);
            CheckReturnValue(ret);

            return value;
        }

        /// <inheritdoc />
        public bool WaitOne(int timeoutMilliseconds)
        {
            // Timed wait currently not supported on Linux, provided timeout is ignored unless it's less than zero in which case we wait indefinitely.
            int ret = 0;
            if (timeoutMilliseconds < 0)
            {
                ret = Ipc.SemWait(m_semaphore);
            }
            else
            {
                ret = Ipc.SemTryWait(m_semaphore);
            }

            CheckReturnValue(ret);

            return true;
        }

        /// <summary>
        /// Checks the return value and throws an exception with the errno if an operation failed.
        /// </summary>
        private void CheckReturnValue(int ret, [CallerMemberName] string op = "")
        {
            if (ret != 0)
            {
                throw new Exception($"{op} failed for semaphore '{Name}' with errno {ret}");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <nodoc/>
        public void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
                int ret = Ipc.SemClose(m_semaphore);
                ret = Ipc.SemUnlink(Name);

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}
