// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class PipDetails : PipRefWithDetails
    {
        public PipDetails(CachedGraph graph, Pip pip)
            : base(graph.Context, pip)
        {
            LongDescription = pip.GetDescription(graph.Context);
            Tags.AddRange(pip.Tags.Select(tag => new TagRef(graph.Context, tag)));
            Dependencies.AddRange(GetPipRefs(graph.Context, graph.PipGraph.RetrievePipImmediateDependencies(pip)));
            Dependents.AddRange(GetPipRefs(graph.Context, graph.PipGraph.RetrievePipImmediateDependents(pip)));
        }

        public string LongDescription { get; set; }

        public List<TagRef> Tags { get; } = new List<TagRef>(0);

        public List<PipRefWithDetails> Dependencies { get; } = new List<PipRefWithDetails>();

        public List<PipRefWithDetails> Dependents { get; } = new List<PipRefWithDetails>();


        private IEnumerable<PipRefWithDetails> GetPipRefs(PipExecutionContext context, IEnumerable<Pip> pips)
        {
            return pips
                .Where(pip => pip.PipType != PipType.HashSourceFile && !pip.PipType.IsMetaPip())
                .Select(dependency => new PipRefWithDetails(context, dependency));

        }
    }
}
