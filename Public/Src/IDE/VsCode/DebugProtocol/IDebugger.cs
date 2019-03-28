// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace VSCode.DebugProtocol
{
    public interface IDebugger
    {
        void ShutDown();

        void SendEvent<T>(IEvent<T> e);

        ISession Session { get; }
    }
}
