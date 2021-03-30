// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IServer : IDisposable
    {
        void Start(int port);
        Task ShutdownAsync();
        Task DisposeAsync();
    }
}