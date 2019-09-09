// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Symbol.App.Core;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Telemetry;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Microsoft.VisualStudio.Services.Symbol.WebApi;
using Newtonsoft.Json;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Client for Artifact Services symbols endpoint.
    /// </summary>
    public sealed class VsoSymbolClient : ISymbolClient
    {
        private const DebugEntryCreateBehavior DefaultDebugEntryCreateBehavior = DebugEntryCreateBehavior.ThrowIfExists;

        private static IAppTraceSource Tracer => SymbolAppTraceSource.SingleInstance;

        private readonly ILogger m_logger;
        private readonly SymbolConfig m_config;
        private readonly ISymbolServiceClient m_symbolClient;
        private readonly CancellationTokenSource m_cancellationSource;
        private CancellationToken CancellationToken => m_cancellationSource.Token;
        private string m_requestId;

        private VssCredentials GetCredentials() =>
            new VsoCredentialHelper(m => m_logger.Verbose(m))
                .GetCredentials(m_config.Service, true, null);

        private ArtifactHttpClientFactory GetFactory() =>
            new ArtifactHttpClientFactory(
                credentials: GetCredentials(),
                httpSendTimeout: m_config.HttpSendTimeout,
                tracer: Tracer,
                verifyConnectionCancellationToken: CancellationToken);

        private Uri ServiceEndpoint => m_config.Service;

        private string RequestName => m_config.Name;

        private string RequestId
        {
            get
            {
                Contract.Requires(!string.IsNullOrEmpty(m_requestId));
                return m_requestId;
            }
        }

        /// <nodoc />
        public VsoSymbolClient(ILogger logger, SymbolConfig config)
        {
            m_logger = logger;
            m_config = config;
            m_cancellationSource = new CancellationTokenSource();

            m_logger.Info(I($"[{nameof(VsoSymbolClient)}] Using drop config: {JsonConvert.SerializeObject(m_config)}"));

            m_symbolClient = CreateSymbolServiceClient();
        }

        private ISymbolServiceClient CreateSymbolServiceClient()
        {
            var client = new SymbolServiceClient(
                ServiceEndpoint,
                GetFactory(),
                Tracer,
                new SymbolServiceClientTelemetry(Tracer, ServiceEndpoint, enable: m_config.EnableTelemetry));

            return client;
        }

        /// <summary>
        /// Queries the endpoint for the RequestId. 
        /// This method should be called only after the request has been created, otherwise, it will throw an exception.
        /// </summary>
        /// <remarks>
        /// On workers, m_requestId won't be initialized, so we need to query the server for the right value.
        /// </remarks>
        private async Task EnsureRequestIdInitalizedAsync()
        {
            if (string.IsNullOrEmpty(m_requestId))
            {
                var result = await m_symbolClient.GetRequestByNameAsync(RequestName, CancellationToken);
                m_requestId = result.Id;
            }
        }

        /// <summary>
        /// Creates a symbol request.
        /// </summary>
        public async Task<Request> CreateAsync(CancellationToken token)
        {
            var result = await m_symbolClient.CreateRequestAsync(RequestName, token);

            m_requestId = result.Id;

            return result;
        }

        /// <inheritdoc />
        public Task<Request> CreateAsync() => CreateAsync(CancellationToken);

        /// <inheritdoc />
        public async Task<AddDebugEntryResult> AddFileAsync(SymbolFile symbolFile)
        {
            Contract.Requires(symbolFile.IsIndexed, "File has not been indexed.");
            Contract.Requires(symbolFile.DebugEntries.Count > 0, "File contains no symbol data.");

            await EnsureRequestIdInitalizedAsync();

            var result = await m_symbolClient.CreateRequestDebugEntriesAsync(
                RequestId,
                symbolFile.DebugEntries.Select(e => CreateDebugEntry(e)),
                DefaultDebugEntryCreateBehavior,
                CancellationToken);

            var entriesWithMissingBlobs = result.Where(e => e.Status == DebugEntryStatus.BlobMissing).ToList();

            if (entriesWithMissingBlobs.Count > 0)
            {
                // All the entries are based on the same file, so we need to call upload only once.

                // make sure that the file is on disk
                var file = await symbolFile.EnsureMaterializedAsync();

                var uploadResult = await m_symbolClient.UploadFileAsync(
                    // uploading to the location set by the symbol service
                    entriesWithMissingBlobs[0].BlobUri,
                    RequestId,
                    symbolFile.FullFilePath,
                    entriesWithMissingBlobs[0].BlobIdentifier,
                    CancellationToken);

                m_logger.Info($"File: '{symbolFile.FullFilePath}' -- upload result: {uploadResult.ToString()}");

                entriesWithMissingBlobs.ForEach(entry => entry.BlobDetails = uploadResult);

                entriesWithMissingBlobs = await m_symbolClient.CreateRequestDebugEntriesAsync(
                    RequestId,
                    entriesWithMissingBlobs,
                    DefaultDebugEntryCreateBehavior,
                    CancellationToken);

                Contract.Assert(entriesWithMissingBlobs.All(e => e.Status != DebugEntryStatus.BlobMissing), "Entries with non-success code are present.");

                return AddDebugEntryResult.UploadedAndAssociated;
            }

            return AddDebugEntryResult.Associated;
        }

        /// <summary>
        /// Finalizes a symbol request.
        /// </summary>        
        public async Task<Request> FinalizeAsync(CancellationToken token)
        {
            var result = await m_symbolClient.FinalizeRequestAsync(
                RequestId,
                ComputeExpirationDate(m_config.Retention),
                // isUpdateOperation == true => request will be marked as 'Sealed', 
                // i.e., no more DebugEntries could be added to it 
                isUpdateOperation: false,
                token);

            return result;
        }

        /// <inheritdoc />
        public Task<Request> FinalizeAsync() => FinalizeAsync(CancellationToken);

        private DateTime ComputeExpirationDate(TimeSpan retention)
        {
            return DateTime.UtcNow.Add(retention);
        }

        /// <nodoc />
        public void Dispose()
        {
            m_symbolClient.Dispose();
        }

        private static DebugEntry CreateDebugEntry(DebugEntryData data)
        {
            return new DebugEntry()
            {
                BlobIdentifier = data.BlobIdentifier,
                ClientKey = data.ClientKey,
                InformationLevel = data.InformationLevel
            };
        }
    }
}