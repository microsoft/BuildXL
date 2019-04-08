// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

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
