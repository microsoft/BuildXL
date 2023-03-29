// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <nodoc />
    public interface ILogMessageObserver
    {
        /// <nodoc />
        void OnMessage(Diagnostic diagnostic);
    }
}