// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Newtonsoft.Json;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Straightforward implementation of ISymbolIndexer
    /// </summary>
    public class SymbolIndexer : ISymbolIndexer
    {
        /// <summary>
        /// Field name of the client id in the javascript sourcemap.
        /// The name is prefixed with x_microsoft per the spec: https://sourcemaps.info/spec.html#h.ghqpj1ytqjbm
        /// </summary>
        public const string SourceMapSymbolClientKeyField = "x_microsoft_symbol_client_key";

        private readonly SymstoreUtil m_symstoreUtil;

        /// <nodoc />
        public SymbolIndexer(IAppTraceSource tracer)
        {
            m_symstoreUtil = new SymstoreUtil(tracer);
        }

        /// <inheritdoc/>
        public IEnumerable<DebugEntryData> GetDebugEntries(FileInfo file, bool calculateBlobId = false)
        {
            Contract.Requires(File.Exists(file.FullName));

            // Symbol does not support .js.map files yet. Talks are underway with the artifact of adding it there.
            // To support javascript's symbols called sourcemaps, in office in the mean time, we can special case
            // the logic here.
            // Bug 1844922: Tracks removing this code once it is shipped with the artifacts symbol library and that
            // version is ingested into buildxl.
            if (file.FullName.EndsWith(".js.map", System.StringComparison.OrdinalIgnoreCase))
            {
                return GetJsMapDebugEntries(file, calculateBlobId);
            }

            var entries = m_symstoreUtil.GetDebugEntryData(
                file.FullName,
                new[] { file.FullName },
                calculateBlobId,
                // Currently, only file-deduped (VsoHash) symbols are supported
                isChunked: false);

            return entries;
        }

        /// <inheritdoc/>
        private IEnumerable<DebugEntryData> GetJsMapDebugEntries(FileInfo file, bool calculateBlobId = false)
        {
            return GetJsMapDebugEntryAsync(file, calculateBlobId).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Extracts the debug entry for a javascript sourcemap file.
        /// It will try to extract the client key from the sourcemap, so that
        /// the tool that writes the sourcemap has control over they key.
        /// It will fall back to the sha256 of the sourcemap as the client key
        /// when it can't be found.
        /// </summary>
        internal static async Task<DebugEntryData[]> GetJsMapDebugEntryAsync(FileInfo file, bool calculateBlobId = false)
        {
            var fileName = file.FullName;
            string clientKey = TryGetSymbolClientKeyFromJsMap(fileName);
            if (clientKey == null)
            {
                // If the .js.map file file does not contain the proper info, use content hash as a fallback
                try
                {
                    using (var fileStream = FileStreamUtility.OpenFileStreamForAsync(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        var hash = await HashInfoLookup.GetContentHasher(HashType.SHA256).GetContentHashAsync(fileStream);
                        var clientId = hash.ToHex().ToLowerInvariant();
                        clientKey = CreateClientKey(clientId, Path.GetFileName(fileName));
                    }
                }
                catch (IOException)
                {
                    return new DebugEntryData[0];
                }
                catch (UnauthorizedAccessException)
                {
                    return new DebugEntryData[0];
                }
            }

            BlobIdentifier blobId = null;
            if (calculateBlobId)
            {
                var blobDescriptor = await FileBlobDescriptor.CalculateAsync(file.DirectoryName, chunkDedup: false, file.Name, FileBlobType.File, CancellationToken.None);
                blobId = blobDescriptor.BlobIdentifier;
            }

            return new[] {
                new DebugEntryData()
                {
                    BlobIdentifier = blobId,
                    ClientKey = clientKey,
                    InformationLevel = DebugInformationLevel.Private
                }
            };
        }

        /// <summary>
        /// This function opens the given file and parses it as Json to extract a top level field
        /// that contains the symbol client key. It will try to parse as little as possible. 
        /// As soon as it finds the key, it will use that value and stop parsing.
        /// It is the assumption that the symbol tools will put the value before the large cunks of the data.
        /// </summary>
        internal static string TryGetSymbolClientKeyFromJsMap(string filePath)
        {
            string clientId = null;
            string fileName = null;
            try
            {
                using (var streamReader = new StreamReader(filePath))
                {
                    var reader = new JsonTextReader(streamReader);

                    // Read into the file and expect a top-level object.
                    if (reader.Read() && reader.TokenType == JsonToken.StartObject)
                    {
                        // Loop over all members of the object
                        while (reader.TokenType != JsonToken.EndObject)
                        {
                            // If the current property is the clientid field
                            if (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                            {
                                if (string.Equals(reader.Value as string, SourceMapSymbolClientKeyField, System.StringComparison.Ordinal))
                                {
                                    clientId = reader.ReadAsString();
                                } 
                                else if (string.Equals(reader.Value as string, "file", System.StringComparison.Ordinal))
                                {
                                    fileName = reader.ReadAsString();
                                }
                                else 
                                {
                                    reader.Skip();
                                }

                                // if we have both fileName and clientId extracted, return the value early, 
                                // no need to fully parse all json in the file. The shared tools we create that add this 
                                // information to the symbol map make sure that the value is close to  the beginning of the file.
                                if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(clientId) )
                                {
                                    return CreateClientKey(clientId, fileName);
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            { 
                return null;
            }
            catch (JsonException)
            {
                return null;
            }

            // Did not find the client key and file
            return null;
        }

        private static string CreateClientKey(string clientId, string fileName)
        {
            return $"{fileName}/{clientId}/{fileName}";
        }
    }
}
