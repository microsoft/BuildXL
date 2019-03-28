// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.DScript.Debugger
{
    public class EvaluationTimeoutException : DebuggerTestException
    {
        public EvaluationTimeoutException()
            : base("DScript evaluation timed out.") { }
    }
}
