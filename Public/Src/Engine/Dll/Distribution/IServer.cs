// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IServer : IDisposable
    {
        void Start(int port);
        Task StartKestrel(int port, Action<object> configure);
        Task ShutdownAsync();
        Task DisposeAsync();
    }
}