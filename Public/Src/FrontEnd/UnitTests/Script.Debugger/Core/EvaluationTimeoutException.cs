// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Test.DScript.Debugger
{
    public class EvaluationTimeoutException : DebuggerTestException
    {
        public EvaluationTimeoutException()
            : base("DScript evaluation timed out.") { }
    }
}
