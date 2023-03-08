// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// An interface for executing IPC operations on the server side.
    /// </summary>
    /// <remarks>
    /// Implementations of this interface should be thread-safe, since
    /// <see cref="IServer"/> may call the <see cref="ExecuteAsync"/> method concurrently.
    /// </remarks>
    public interface IIpcOperationExecutor
    {
        /// <summary>
        /// Executes given command.  Doesn't need to worry about not throwing exceptions,
        /// because <see cref="IServer"/> is responsible for handling exceptions any thrown
        /// from here.
        /// </summary>
        Task<IIpcResult> ExecuteAsync(int id, [NotNull]IIpcOperation op);
    }
}
