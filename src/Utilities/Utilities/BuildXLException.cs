// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A general, recoverable exception thrown by BuildXL. Callers can safely try - catch for BuildXLException
    /// without also catching exceptions that indicate a programming error.
    /// </summary>
    [Serializable]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    public sealed class BuildXLException : Exception
    {
        private const string ExceptionRootCauseName = "ExceptionRootCause";
#pragma warning disable CA2235 // Mark all non-serializable fields
        private readonly ExceptionRootCause m_rootCause;
#pragma warning restore CA2235 // Mark all non-serializable fields

        /// <summary>
        /// Creates a BuildXLException with the given diagnostic message, without any inner exception as the root cause.
        /// Instead, a well-known <see cref="ExceptionRootCause"/> may be provided.
        /// </summary>
        public BuildXLException([Localizable(false)] string message, ExceptionRootCause rootCause = ExceptionRootCause.Unknown)
            : base(message)
        {
            m_rootCause = rootCause;
        }

        /// <summary>
        /// Creates a BuildXLException with the given diagnostic message that was caused by the given
        /// <paramref name="innerException" />. If a <paramref name="rootCause"/> is not provided, the root cause
        /// is derived from the inner exception.
        /// </summary>
        public BuildXLException([Localizable(false)]string message, Exception innerException, ExceptionRootCause rootCause = ExceptionRootCause.Unknown)
            : base(message, innerException)
        {
            if (rootCause != ExceptionRootCause.Unknown)
            {
                m_rootCause = rootCause;
            }
            else if (innerException != null)
            {
                m_rootCause = ExceptionUtilities.AnalyzeExceptionRootCause(innerException);
            }
            else
            {
                m_rootCause = ExceptionRootCause.Unknown;
            }
        }

        private BuildXLException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            m_rootCause = (ExceptionRootCause)info.GetInt32(ExceptionRootCauseName);
        }

        /// <inheritdoc />
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(ExceptionRootCauseName, (int)m_rootCause);
            base.GetObjectData(info, context);
        }

        /// <summary>
        /// Gets the error code (Win32 error code e.g. <c>ERROR_ACCESS_DENIED</c> if possible) of the innermost exception
        /// </summary>
        /// <remarks>
        /// <see cref="Win32Exception.NativeErrorCode"/> is most valuable if that is the innermost exception; it should correspond
        /// to an error captured with <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/>, or an NTSTATUS. Note that using <see cref="Exception.HResult"/>
        /// in that case (set to a generic <c>E_FAIL</c>) erases very valuable information.
        /// </remarks>
        public int LogEventErrorCode => this.GetLogEventErrorCode();

        /// <summary>
        /// Gets the full message
        /// </summary>
        public string LogEventMessage => this.GetLogEventMessage();

        /// <summary>
        /// Well-known root cause for this chain of exceptions.
        /// Root causes are generally inherited from 'inner' exceptions;
        /// the root cause of some inner exception may have been determined by <see cref="ExceptionUtilities.AnalyzeExceptionRootCause"/>.
        /// </summary>
        public ExceptionRootCause RootCause
        {
            get { return m_rootCause; }
        }
    }
}
