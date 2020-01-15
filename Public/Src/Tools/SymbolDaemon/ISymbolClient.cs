// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Symbol.WebApi;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Abstraction for communicating with the Symbol service endpoint.
    /// </summary>
    public interface ISymbolClient : IDisposable
    {
        /// <summary>
        /// Task for creating a symbol publishing request. 
        /// </summary>        
        Task<Request> CreateAsync();

        /// <summary>
        /// Task for finalizing a symbol publishing request. 
        /// </summary>      
        Task<Request> FinalizeAsync();

        /// <summary>
        /// Task for adding symbol data to the request.
        /// </summary>
        /// <param name="symbolFile">A file that contains symbol data. The file must have been indexed prior calling this method.</param>        
        Task<AddDebugEntryResult> AddFileAsync(SymbolFile symbolFile);
    }
}