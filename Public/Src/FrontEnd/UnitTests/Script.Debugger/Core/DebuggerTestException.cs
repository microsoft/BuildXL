// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Test.DScript.Debugger
{
    public class DebuggerTestException : Exception
    {
        public DebuggerTestException()
            : base() { }

        public DebuggerTestException(string message)
            : base(message) { }
    }
}
