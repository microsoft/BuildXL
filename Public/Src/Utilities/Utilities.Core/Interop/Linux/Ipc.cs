// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using BuildXL.Interop.Unix;
using static BuildXL.Interop.Dispatch;
using static BuildXL.Interop.Unix.Impl_Linux;

namespace BuildXL.Interop.Linux
{
    /// <summary>
    /// Native Linux IPC functions.
    /// </summary>
    public static class Ipc
    {
        /// <summary>
        /// Max Length of a POSIX named semaphore.
        /// </summary>
        public static int SemaphoreNameMaxLength => IO.NAME_MAX - 4;

        /// <summary>
        /// Create or open an existing semaphore.
        /// </summary>
        public static int SemOpen(string name, uint initialCount, out IntPtr semaphore)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            // O_CREAT will create a new semaphore if one doesn't exist
            // O_EXCL will return an error if the specified semaphore name already exists
            semaphore = sem_open(name, (int)(O_Flags.O_CREAT | O_Flags.O_EXCL), mode: /*0644*/ 0x1a4, value: initialCount);
            if (semaphore == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error();
            }

            return 0;
        }

        /// <summary>
        /// Try wait for the semaphore to be unblocked, return immediately if the semaphore is not set.
        /// </summary>
        public static int SemTryWait(IntPtr semaphore)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            int error = sem_trywait(semaphore);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }

        /// <summary>
        /// Wait with sem_wait, this will wait indefinitely if the semaphore is zero.
        /// </summary>
        public static int SemWait(IntPtr semaphore)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            int error = sem_wait(semaphore);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }

        /// <summary>
        /// Increment the semaphore by one.
        /// </summary>
        public static int SemPost(IntPtr semaphore)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            var error = sem_post(semaphore);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }

        /// <summary>
        /// Get the current value of the semaphore.
        /// </summary>
        public static int SemGetValue(IntPtr semaphore, out int value)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            var error = sem_getvalue(semaphore, out value);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }

        /// <summary>
        /// Close the semaphore without destroying it.
        /// </summary>
        public static int SemClose(IntPtr semaphore)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            var error = sem_close(semaphore);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }

        /// <summary>
        /// Destroy a named semaphore.
        /// </summary>
        public static int SemUnlink(string name)
        {
            if (IsMacOS)
            {
                throw new NotImplementedException();
            }

            var error = sem_unlink(name);
            if (error != 0)
            {
                error = Marshal.GetLastWin32Error();
            }

            return error;
        }
    }
}
