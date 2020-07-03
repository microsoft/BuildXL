// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Engine.Distribution
{
    internal interface IServer : IDisposable
    {
        void Start(int port);
    }
}