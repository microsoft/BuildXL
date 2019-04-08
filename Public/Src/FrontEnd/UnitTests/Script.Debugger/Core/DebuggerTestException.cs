// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
