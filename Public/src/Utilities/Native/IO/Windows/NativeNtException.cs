// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// A possibly-recoverable exception wrapping a failed native call. The <see cref="Win32Exception.NativeErrorCode" /> captures the
    /// associated <see cref="NtStatus"/> value. The <see cref="Exception.Message" />
    /// accounts for the native code as well as a human readable portion.
    /// </summary>
    /// <remarks>
    /// This is much like <see cref="Win32Exception"/>, but the message field contains the caller-provided part in addition
    /// to the name of the NTSTATUS code (we can't call FormatMessage for those).
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors",
        Justification = "We don't need exceptions to cross AppDomain boundaries.")]
    [Serializable]
    public sealed class NativeNtException : Win32Exception
    {
        /// <summary>
        /// Creates an exception representing a native failure (with a corresponding Win32 error code).
        /// The exception's <see cref="Exception.Message" /> includes the error code, a system-provided message describing it,
        /// and the provided application-specific message prefix (e.g. "Unable to open log file").
        /// </summary>
        public NativeNtException(NtStatus status, string messagePrefix = null)
            : base(unchecked((int)status.Value), GetFormattedMessageForNativeErrorCode(status, messagePrefix))
        {
            HResult = unchecked((int)status.Value);
        }

        /// <summary>
        /// Returns a human readable error string for a native NTSTATUS, like <c>Native: Can't access the log file (STATUS_ACCESS_DENIED)</c>.
        /// The message prefix (e.g. "Can't access the log file") is optional.
        /// </summary>
        public static string GetFormattedMessageForNativeErrorCode(NtStatus status, string messagePrefix = null)
        {
            return !string.IsNullOrEmpty(messagePrefix)
                ? I($"Native: {messagePrefix} ({status})")
                : I($"Native: {status}");
        }
    }
}
