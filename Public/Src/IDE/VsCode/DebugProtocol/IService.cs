// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

namespace VSCode.DebugProtocol
{
    /// <summary>
    ///     Debugger service, which should allow a concrete <see cref="IDebugger"/> to connect to it.
    /// </summary>
    public interface IService
    {
        /// <summary>Start asynchronously and wait for an <see cref="IDebugger"/> to connect.</summary>
        Task<IDebugger> StartAsync();

        /// <summary>Shut the service down (not accepting any new debuggers to connect).</summary>
        void ShutDown();
    }
}
