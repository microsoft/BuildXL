// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Special version of a parsing queue responsible for parsing and binding source specs for fingerprint computation.
    /// </summary>
    internal sealed class FingerprintComputationParsingQueue : ModuleParsingQueue
    {
        /// <nodoc />
        public FingerprintComputationParsingQueue(WorkspaceProvider workspaceProvider, WorkspaceConfiguration workspaceConfiguration, IModuleReferenceResolver moduleReferenceResolver)
            : base(workspaceProvider: workspaceProvider, workspaceConfiguration: workspaceConfiguration, moduleReferenceResolver: moduleReferenceResolver, designatedPrelude: null, configurationModule: null)
        {
        }

        /// <inhreritdoc />
        public override Possible<ISourceFile>[] ParseAndBindSpecs(SpecWithOwningModule[] specs)
        {
            // It is very important to dispose the cancellation registration for parse/bind case as well.
            cancellationTokenChain.Dispose();

            // Not using queue here for now.
            var result = ParallelAlgorithms.ParallelSelect(
                specs,
                spec =>
                {
                    // Parsing and binding the given spec.
                    return
                        TryParseSpec(spec).GetAwaiter().GetResult()
                        .Then(ps =>
                        {
                            BindSourceFile(new ParsedSpecWithOwningModule(parsedFile: ps, owningModule: spec.OwningModule));

                            return ps.BindDiagnostics.Count == 0 ? new Possible<ISourceFile>(ps) : new BindingFailure(spec.OwningModule.Descriptor, ps);
                        });
                },
                DegreeOfParallelism,
                CancellationToken);

            return result.ToArray();
        }
    }
}
