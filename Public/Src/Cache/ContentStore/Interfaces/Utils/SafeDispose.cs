// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

#nullable disable

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    /// Handles disposal of objects checking for null, handling thread safety, and replacing instances with null (where
    /// necessary)
    /// </summary>
    public static class SafeDispose
    {
        /// <summary>
        /// Dispose of <paramref name="disposable"/> and sets the reference to null.
        /// </summary>
        public static void DisposeAndSetNull<T>(ref T disposable)
            where T : class, IDisposable
        {
            IDisposable oldValue = Interlocked.Exchange(ref disposable, null);
            oldValue?.Dispose();
        }
    }
}
