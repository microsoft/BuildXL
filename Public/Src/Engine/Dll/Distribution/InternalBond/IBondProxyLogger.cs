// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Distribution.InternalBond
{
    internal interface IBondProxyLogger
    {
        void LogSuccessfulCall(LoggingContext loggingContext, string functionName, uint retry);

        void LogFailedCall(LoggingContext loggingContext, string functionName, uint retry, Failure failure);

        void LogCallException(LoggingContext loggingContext, string functionName, uint retry, Exception ex);
    }
}
#endif
