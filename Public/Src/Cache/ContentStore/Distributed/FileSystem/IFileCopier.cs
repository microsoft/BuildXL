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
    /// Represents an interface that allows copying files from a remote source to a local stream
    /// </summary>
    public interface IRemoteFileCopier
    {
        /// <summary>
        /// Return opaque content location data that will be set for locally produced content.
        /// </summary>
        /// <param name="cacheRoot">The cache root path to the local cache.</param>
        /// <remarks>
        /// Returned location data is saved to the ContentLocationStore
        /// and used by peers for downloading content from local machine
        /// e.g. An implementation could be \\machine\cacheRoot
        /// </remarks>
        MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot);

        /// <summary>
        /// Copies a file represented by the path into the stream specified.
        /// </summary>
        Task<CopyFileResult> CopyToAsync(OperationContext context, ContentLocation sourceLocation, Stream destinationStream, CopyOptions options);
    }

    /// <summary>
    /// Represents a location of content on a machine
    /// </summary>
    [StructGenerators.StructRecord]
    public readonly partial struct ContentLocation
    {
        /// <nodoc />
        public MachineLocation Machine { get; }

        /// <nodoc />
        public ContentHash Hash { get; }

        /// <summary>
        /// If true, then the location is not a real location obtained from the local or the global store, but just a machine from the build ring.
        /// </summary>
        public bool FromRing { get; }

        /// <nodoc />
        public ContentLocation(MachineLocation machine, ContentHash hash, bool fromRing = false)
        {
            Machine = machine;
            Hash = hash;
            FromRing = fromRing;
        }

        /// <inheritdoc />
        public override string ToString() => Machine.ToString();
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
        Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine, CopyOptions options);

        /// <summary>
        /// Deletes content from a target machine
        /// </summary>
        Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine);
    }
}
