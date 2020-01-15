// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
