// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Explorer.Server.Models;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("b")]
    [ApiController]
    public class PipController : LoadController
    {
        public PipController(IMemoryCache memoryCache, IHostingEnvironment env)
            : base(memoryCache, env)
        {
        }

        [Route("{sessionId}/pips")]
        public async Task<ActionResult<PageResult<PipRefWithDetails>>> GetAsync(string sessionId, int? page)
        {
            int pageSize = 100;

            var graph = await GetCachedGraphAsync(sessionId);
            var pipTable = graph.PipTable;

            var filter = new PipFilter(Request.Query);
            IEnumerable<PipReference> searchResult = null;
            if (filter.SemiStableHash.HasValue)
            {
                searchResult = pipTable.StableKeys
                    .Where(pipId => pipTable.GetPipSemiStableHash(pipId) == filter.SemiStableHash)
                    .Select(pipId => new PipReference(pipTable, pipId, PipQueryContext.Explorer));
            }
            else // SemiStablehash trumps all other filters, so only do other filters after this one
            {
                if (filter.PipType.HasValue)
                {
                    searchResult = graph.PipGraph.RetrievePipReferencesOfType(filter.PipType.Value);
                }

                if (!string.IsNullOrEmpty(filter.Description))
                {
                    var filterDescription = filter.Description;
                    searchResult = (searchResult ?? AllPipIds(pipTable))
                        .Where(pipRef =>
                            pipRef.HydratePip()
                            .GetDescription(graph.Context)
                            .Contains(filterDescription, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (searchResult == null)
            {
                searchResult = AllPipIds(pipTable);
            }

            var pipRefs = searchResult.Skip((page ?? 0) & pageSize).Take(pageSize);
            var context = graph.Context;

            var results = pipRefs.Select(pipRef => new PipRefWithDetails(context, pipRef.HydratePip())).ToArray();

            return new PageResult<PipRefWithDetails>()
            {
                Page = page ?? 0,
                Count = results.Length,
                Items = results
            };
        }

        private IEnumerable<PipReference> AllPipIds(PipTable pipTable)
        {
            return pipTable.StableKeys
                .Where(pipId => pipTable.GetPipType(pipId) != PipType.HashSourceFile)
                .Select(pipId => new PipReference(pipTable, pipId, PipQueryContext.Explorer));
        }

        [Route("{sessionId}/pips/{pipId}")]
        public async Task<ActionResult<PipDetails>> GetPipAsync(string sessionId, uint pipId)
        {
            var graph = await GetCachedGraphAsync(sessionId);
            var pip = graph.PipGraph.GetPipFromUInt32(pipId);

            switch (pip.PipType)
            {
                case PipType.Process:
                    return new ProcessDetails(graph, (Process)pip);
                default:
                    return new PipDetails(graph, pip);
            }
        }


        private class PipFilter
        {
            public PipFilter(IQueryCollection queryCollection)
            {
                if (queryCollection.TryGetValue("semiStableHash", out var semiStableHashValues))
                {
                    var semiStableHashValue = semiStableHashValues.Last();
                    if (semiStableHashValue.StartsWith(Pip.SemiStableHashPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        semiStableHashValue = semiStableHashValue.Substring(Pip.SemiStableHashPrefix.Length);
                    }

                    try
                    {
                        SemiStableHash = Convert.ToInt64(semiStableHashValue, 16);
                    }
                    catch (FormatException)
                    {
                        // Set to 0 so no pip will match.
                        SemiStableHash = -1;
                    }
                }

                if (queryCollection.TryGetValue("kind", out var pipTypeValues))
                {
                    var pipType = pipTypeValues.Last();
                    switch (pipType)
                    {
                        case "process":
                            PipType = Pips.Operations.PipType.Process;
                            break;
                        case "copyFile":
                            PipType = Pips.Operations.PipType.CopyFile;
                            break;
                        case "writeFile":
                            PipType = Pips.Operations.PipType.WriteFile;
                            break;
                        case "sealDirectory":
                            PipType = Pips.Operations.PipType.SealDirectory;
                            break;
                        case "ipc":
                            PipType = Pips.Operations.PipType.Ipc;
                            break;
                        case "value":
                            PipType = Pips.Operations.PipType.Value;
                            break;
                        case "specFile":
                            PipType = Pips.Operations.PipType.SpecFile;
                            break;
                        case "module":
                            PipType = Pips.Operations.PipType.Module;
                            break;
                    }
                }

                if (queryCollection.TryGetValue("description", out var descriptionValues))
                {
                    Description = descriptionValues.Last();
                }
            }

            public long? SemiStableHash { get; }

            public PipType? PipType { get; }
            
            public string Description { get; }
        }
    }
}
