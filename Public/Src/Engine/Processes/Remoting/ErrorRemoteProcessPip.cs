// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Error remote process pip.
    /// </summary>
    internal class ErrorRemoteProcessPip : IRemoteProcessPip
    {
        /// <inheritdoc/>
        public Task<IRemoteProcessPipResult> Completion { init; get; }

        /// <summary>
        /// Creates an instance of <see cref="ErrorRemoteProcessPip"/>.
        /// </summary>
        public ErrorRemoteProcessPip(string error) => Completion = Task.FromResult((IRemoteProcessPipResult)new ErrorRemoteProcessPipResult(error));

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
