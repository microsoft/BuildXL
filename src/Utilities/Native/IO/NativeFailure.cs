// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// <see cref="Failure" /> type wrapping a failed native API call.
    /// </summary>
    public sealed class NativeFailure : Failure
    {
        /// <summary>
        /// Native error code as returned from <c>GetLastError</c> or similar API.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; }

        /// <nodoc />
        public NativeFailure(int nativeErrorCode)
            : this(nativeErrorCode, null)
        {
        }

        /// <nodoc />
        public NativeFailure(int nativeErrorCode, string message)
        {
            NativeErrorCode = nativeErrorCode;
            Message = message;
        }

        /// <summary>
        /// Creates a failure from <see cref="NativeWin32Exception"/>.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static NativeFailure CreateFromException(NativeWin32Exception exception)
        {
            return new NativeFailure(exception.NativeErrorCode, exception.Message);
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return NativeWin32Exception.GetFormattedMessageForNativeErrorCode(NativeErrorCode, messagePrefix: Message);
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(DescribeIncludingInnerFailures());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
