// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing.Internal;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path using absolute paths.
    /// </summary>
    public interface IAbsolutePathRemoteFileCopier : IRemoteFileCopier<AbsolutePath>, IFileExistenceChecker<AbsolutePath>
    {

    }

    /// <summary>
    /// Copies files to another machine.
    /// </summary>
    public interface IContentCommunicationManager
    {
        /// <summary>
        /// Requests another machine to copy a file.
        /// </summary>
        /// <remarks>
        /// This version is not used in Test/Prod environment but please, don't remove it, because we may use it in the future
        /// for other scenarios, for instance, during pin operaiton.
        /// </remarks>
        Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine);

        /// <summary>
        /// Pushes content to a target machine.
        /// </summary>
        Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine);

        /// <summary>
        /// Deletes content from a target machine
        /// </summary>
        /// <param name="context"></param>
        /// <param name="hash"></param>
        /// <param name="targetMachine"></param>
        /// <returns></returns>
        Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine);
    }
}
