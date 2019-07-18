// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.VisualStudio.Services.Drop.WebApi;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Abstraction for communicating with a drop service endpoint.
    /// </summary>
    /// <remarks>
    /// All the methods/properties in this interface assume that the concrete <see cref="IDropClient"/> instance
    /// has already been initialized with the necessary drop settings (<see cref="DropConfig"/>).
    /// </remarks>
    public interface IDropClient : IDisposable
    {
        /// <summary>
        /// URL at which the drop can be obtained/viewed.
        /// </summary>
        string DropUrl { get; }

        /// <summary>
        /// Task for performing 'drop create'.
        /// </summary>
        Task<DropItem> CreateAsync();

        /// <summary>
        /// Task for performing 'drop addfile'.
        /// </summary>
        Task<AddFileResult> AddFileAsync([NotNull]IDropItem dropItem);

        /// <summary>
        /// Task for performing 'drop finalize'.
        /// </summary>
        Task<FinalizeResult> FinalizeAsync();

        /// <summary>
        /// Arbitrary statistics to report;
        /// </summary>
        [NotNull]
        IDictionary<string, long> GetStats();
    }

    /// <summary>
    /// Result of the 'AddFile' operation (called on a single file).
    /// </summary>
    public enum AddFileResult
    {
        /// <summary>Indicates that the file was only associated (didn't need to be uploaded).</summary>
        Associated = 0,

        /// <summary>Indicates that the file had to be uploaded.</summary>
        UploadedAndAssociated = 1,
    }

    /// <summary>
    /// Placeholder for future result that might be returned by <see cref="IDropClient.FinalizeAsync"/>.
    /// </summary>
    public sealed class FinalizeResult
    { }
}
