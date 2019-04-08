// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Class that keeps a global week reference to a front-end instance.
    /// </summary>
    /// <remarks>
    /// It is crucial to clean-up front-end related memory once it is no longer needed.
    /// This type is responsible for capturing the handle at front end initialization time
    /// and provides an API to check wheather the object is still alive.
    /// </remarks>
    public static class FrontEndControllerMemoryObserver
    {
        private static WeakReference s_frontEndHandle;

        /// <nodoc />
        public static void CaptureFrontEndReference(object frontEndHandle)
        {
            Contract.Requires(frontEndHandle != null);
            s_frontEndHandle = new WeakReference(frontEndHandle);
        }

        /// <nodoc />
        public static bool IsFrontEndAlive()
        {
            return s_frontEndHandle?.IsAlive == true;
        }
    }
}
