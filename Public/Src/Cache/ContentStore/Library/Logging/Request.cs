// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Logging
{
    internal class Request
    {
        public readonly RequestType Type;
        public readonly DateTime DateTime;
        public readonly int ThreadId;
        public readonly Severity Severity;
        public readonly string Message;

        protected Request(RequestType type)
        {
            Type = type;
        }

        protected Request(RequestType type, DateTime dateTime, int threadId, Severity severity, string message)
        {
            Type = type;
            DateTime = dateTime;
            ThreadId = threadId;
            Severity = severity;
            Message = message;
        }
    }

#pragma warning disable SA1402 // File may only contain a single class

    internal class ShutdownRequest : Request
    {
        public ShutdownRequest()
            : base(RequestType.Shutdown)
        {
        }
    }

    internal class FlushRequest : Request
    {
        public FlushRequest()
            : base(RequestType.Flush)
        {
        }
    }

    internal class LogStringRequest : Request
    {
        public LogStringRequest(DateTime dateTime, int threadId, Severity severity, string message)
            : base(RequestType.LogString, dateTime, threadId, severity, message)
        {
        }
    }

#pragma warning restore SA1402 // File may only contain a single class
}
