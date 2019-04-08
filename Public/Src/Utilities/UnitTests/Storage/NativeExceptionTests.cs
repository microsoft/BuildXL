// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    [Trait("Category", "WindowsOSOnly")]
    public class NativeExceptionTests
    {
        [Fact]
        public void ErrorCodeWithNestedNativeWin32Exception()
        {
            const int ErrorAccessDenied = 5;
            var ex = new BuildXLException(
                "Outer",
                new BuildXLException("Less outer", new NativeWin32Exception(ErrorAccessDenied, "Inner")));

            XAssert.AreEqual(ErrorAccessDenied, ex.LogEventErrorCode);
            string message = ex.LogEventMessage;
            XAssert.IsTrue(message.Contains("Less outer"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("Inner"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("Outer"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("Access is denied"), "Missing substring in {0}", message);
        }

        [Fact]
        public void ErrorCodeWithNestedNativeNtException()
        {
            var ex = new BuildXLException(
                "Outer",
                new BuildXLException("Less outer", new NativeNtException(new NtStatus((uint)NtStatusCode.StatusPipeBroken), "Inner")));

            XAssert.AreEqual(unchecked((int)NtStatusCode.StatusPipeBroken), ex.LogEventErrorCode);
            string message = ex.LogEventMessage;
            XAssert.IsTrue(message.Contains("Less outer"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("Inner"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("Outer"), "Missing substring in {0}", message);
            XAssert.IsTrue(message.Contains("StatusPipeBroken"), "Missing substring in {0}", message);
        }

        [Fact]
        public void NativeWin32ExceptionIncludesSystemFormattedString()
        {
            const int ErrorAccessDenied = 5;

            var ex = new NativeWin32Exception(ErrorAccessDenied, "Prefix");
            XAssert.IsTrue(ex.Message.Contains("Access is denied"));
            XAssert.IsTrue(ex.Message.Contains("Prefix"));
            XAssert.AreEqual(ErrorAccessDenied, ex.NativeErrorCode);
        }

        [Fact]
        public void NativeNtExceptionIncludesCodeName()
        {
            var ex = new NativeNtException(new NtStatus((uint)NtStatusCode.StatusPipeBroken), "Prefix");
            XAssert.IsTrue(ex.Message.Contains("StatusPipeBroken"));
            XAssert.IsTrue(ex.Message.Contains("Prefix"));
            XAssert.AreEqual(unchecked((int)NtStatusCode.StatusPipeBroken), ex.NativeErrorCode);
        }
    }
}
