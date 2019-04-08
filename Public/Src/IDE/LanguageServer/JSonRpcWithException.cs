// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Ide.LanguageServer.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using StreamJsonRpc;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Derived class from JSonRpc that overrides and implements IsFatalException to cause a fail fast
    /// on an unhandled exception in the langauge server.
    /// </summary>
    /// <remarks>
    /// This class enables the DScript language server to produce Windows Error Reporting (Watson) reports.
    /// </remarks>
    internal sealed class JsonRpcWithException : StreamJsonRpc.JsonRpc
    {
        private bool m_failFastOnException;
        private Logger m_logger;
        private LoggingContext m_loggingContext;

        /// <summary>
        /// Indicates whether to fail fast when an unhandled exception is encountered.
        /// </summary>
        public bool FailFastOnException { get; set; }

        /// <summary>
        /// Contains the methods needed to change the process and thread error mode
        /// so that the Watson reporting works for the language server.
        /// </summary>
        /// <remarks>
        /// During CLR initialization, there are many kernel calls made that call
        /// SetErrorMode(NoGPFaultErrorBox) and then restore the value (typically DisplayAllErrros)
        /// after the perform some action.
        /// 
        /// However, if a thread is created while that action is being performed, it inherits the
        /// process default (which could be NoGPFaultErrorBox due to a temporary change made by another
        /// thread).
        /// 
        /// So, to ensure that our Environment.FailFast actually produces a Watson dump, we need
        /// to set the thread error mode (and the process error mode) to DisplayAllErrors before issuing
        /// the FailFast.
        /// 
        /// There is lots of documentation saying that SetErrorMode should not be used in favor of SetThreadErrorMode
        /// but there is still some code in Windows that has not been modified.
        /// </remarks>
        private static class NativeMethods
        {
            /// <summary>
            /// Enumeration of error modes that can be set by a process or thread.
            /// </summary>
            [Flags]
            public enum SetErrorModeEnum : UInt32
            {
                /// <summary>
                /// Use the system default, which is to display all error dialog boxes.
                /// </summary>
                DisplayAllErrors = 0,

                /// <summary>
                /// The system does not display the critical-error-handler message box. Instead, the system sends the error to the calling process.
                /// </summary>
                FailCriticalErrors = 1,

                /// <summary>
                /// The system does not display the Windows Error Reporting dialog.
                /// </summary>
                NoGPFaultErrorBox = 2,

                /// <summary>
                /// The system automatically fixes memory alignment faults and makes them invisible to the application.
                /// It does this for the calling process and any descendant processes. This feature is only supported by certain processor architectures.
                /// For more information, see the Remarks section. 
                /// </summary>
                NoAlignmentFaultExcept = 4,

                /// <summary>
                /// The OpenFile function does not display a message box when it fails to find a file.
                /// Instead, the error is returned to the caller. This error mode overrides the OF_PROMPT flag.
                /// </summary>
                NoOpenFileErrorBox = 0x8000
            }

            /// <summary>
            /// Sets the error handling mode for the process.
            /// </summary>
            [DllImport("kernel32.dll", SetLastError = true)]
            static internal extern uint SetErrorMode(SetErrorModeEnum errorMode);

            /// <summary>
            /// Sets the error handling mode for the current thread.
            /// </summary>
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static internal extern bool SetThreadErrorMode(SetErrorModeEnum newErrorMode, out SetErrorModeEnum oldErrorMode);
        }

        /// <summary>
        /// Constructs the JSonRpcWithException class.
        /// </summary>
        /// <param name="clientStream">The stream to read JSON-RPC messages from.</param>
        /// <param name="serverStream">The stream to write JSON-RPC messages to.</param>
        /// <param name="target">The target object that implements JSON-RPC methods.</param>
        /// <param name="failFastOnException">Indicates whether to fail fast on exceptions. The default is true.</param>
        public JsonRpcWithException(Stream clientStream, Stream serverStream, object target = null, bool failFastOnException = true) : base(clientStream, serverStream, target)
        {
            m_failFastOnException = failFastOnException;
        }

        /// <summary>
        /// Sets the logging context.
        /// </summary>
        public void SetLoggingContext(Logger logger, LoggingContext loggingContext)
        {
            m_logger = logger;
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Called by the JSON-RPC layer when an unhandled exception is received.
        /// </summary>
        /// <remarks>
        /// We are ignoring the fact that we do not check the last error set by SetErrorMode and SetThreadErrorMode
        /// because if it fails, there isn't much we can do. It is a best attempt.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA1806")]
        protected override bool IsFatalException(Exception ex)
        {
            var exceptionString = ex.ToStringDemystified();

            m_logger.LanguageServerUnhandledInternalError(m_loggingContext, exceptionString);

            // Do not fail fast on debug builds, assert instead.
            System.Diagnostics.Debug.Assert(false, exceptionString);

            if (m_failFastOnException)
            {
                // Don't FailFast if a debugger is attached
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debugger.Break();
                    return false;
                }
                else
                {
                    NativeMethods.SetErrorMode(NativeMethods.SetErrorModeEnum.DisplayAllErrors);
                    NativeMethods.SetThreadErrorMode(NativeMethods.SetErrorModeEnum.DisplayAllErrors, out var oldErrorMode);
                    Environment.FailFast("BuildXL DScript language server encountered an unhandled exception", ex);
                }
            }

            return m_failFastOnException;
        }
    };
}
