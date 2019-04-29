// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Stores;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Contains extensions methods for common error handling.
    /// </summary>
    public static class ExceptionHandlingExtensions
    {
        /// <summary>
        /// Returns true if a given exception indicates an access to disposed <see cref="PinContext"/> instance.
        /// </summary>
        /// <remarks>
        /// It is possible due to a race condition to access disposed <see cref="PinContext"/>.
        /// To avoid this case altogether requires a lot of code restructuring so this helper can be used as a stop-gap/mitigation solution.
        /// </remarks>
        public static bool IsPinContextObjectDisposedException(this Exception exception) =>
            exception is ObjectDisposedException odi && odi.ObjectName == nameof(PinContext);
    }
}
