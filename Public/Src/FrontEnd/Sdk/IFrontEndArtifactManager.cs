// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// General artifact provider for the FrontEnd
    /// </summary>
    /// <remarks>
    /// This interface currently handles the saving and retrieval of front end snapshots and specs public facades with their corresponding serialized AST
    /// TODO: Ideally the front end should stop depending on the front end engine abstraction, whose public surface is already too big, and the functionality
    /// should be moved here and exposed as some sort of file system abstraction
    /// </remarks>
    public interface IFrontEndArtifactManager : IDisposable
    {
        /// <nodoc />
        IWorkspaceBindingSnapshot TryLoadFrontEndSnapshot(int expectedSpecCount);

        /// <nodoc />
        void SaveFrontEndSnapshot(IWorkspaceBindingSnapshot snapshot);

        /// <summary>
        /// Tries to retrieve a <see cref="PublicFacadeSpecWithAst"/> from a given path
        /// </summary>
        /// <remarks>
        /// The result is only available if it was stored in the past and the path is not marked as dirty. Returns null otherwise.
        /// </remarks>
        Task<PublicFacadeSpecWithAst> TryGetPublicFacadeWithAstAsync(AbsolutePath path);

        /// <summary>
        /// Stores a <see cref="PublicFacadeSpecWithAst"/> for future retrieval
        /// </summary>
        Task SavePublicFacadeWithAstAsync(PublicFacadeSpecWithAst publicFacadeWithAst);

        /// <summary>
        /// Stores the file content for a public facade at a given path for future retrieval
        /// </summary>
        Task SavePublicFacadeAsync(AbsolutePath path, FileContent publicFacade);

        /// <summary>
        /// Stores a serialized AST at a given path for future retrieval
        /// </summary>
        Task SaveAstAsync(AbsolutePath path, ByteContent content);

        /// <summary>
        /// Notifies that a collection of paths are dirty and should not be used as public facades.
        /// </summary>
        /// <remarks>
        /// All paths are assumed dirty until notified otherwise. After this notification,
        /// all paths outside of the provided collection are assumed to be safe to use, if available
        /// </remarks>
        void NotifySpecsCannotBeUsedAsFacades(IEnumerable<AbsolutePath> absolutePaths);

        /// <summary>
        /// Returns the content of a given file and flags the path as tracked.
        /// </summary>
        Task<Possible<FileContent, RecoverableExceptionFailure>> TryGetFileContentAsync(AbsolutePath path);
    }
}
