// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Test.DScript.Debugger
{
    public class EvaluationBlockedUponTestCompletionException : DebuggerTestException
    {
        public EvaluationBlockedUponTestCompletionException(string msg)
            : base(msg) { }
    }
}
