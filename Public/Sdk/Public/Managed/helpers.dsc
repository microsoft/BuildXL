// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";
import * as Csc    from "Sdk.Managed.Tools.Csc";

namespace Helpers {

    export declare const qualifier : Shared.TargetFrameworks.All;

    /**
     * Helper method that computes the compile closure based on a set of references.
     * This helper is for tools that gets a set of managed referneces and need to have the list of binaries that are used for compile.
     * Note that this function explicitly does not recurse on nuget packages where the nuget default is to do so.
     */
    @@public
    export function computeCompileClosure(framework: Shared.Framework, references: Shared.Reference[]) : Shared.Binary[] {
        let compile = MutableSet.empty<Shared.Binary>();
        let allReferences = [
            ...(references || []),
            ...framework.standardReferences,
        ];

        for (let ref of allReferences) {
            if (Shared.isBinary(ref))
            {
                compile.add(ref);
            }
            else if (Shared.isAssembly(ref))
            {
                if (ref.compile) {
                    compile.add(ref.compile);
                }
            }
            else if (Shared.isManagedPackage(ref))
            {
                if (ref.compile) {
                    compile.add(...ref.compile);
                }
            }
            else if (ref !== undefined)
            {
                Contract.fail("Unexpected reference added to this project:" + ref);
            }
        }

        return compile.toArray();
    };

    /**
     * Computes a transitive closure or managed binaries for the given list of references.
     * Standard references of the supplied framework are automatically included.
     * You can control whether to follow the compile or the runtime axis using the optional compile argument.
     */
    @@public
    export function computeTransitiveReferenceClosure(framework: Shared.Framework, references: Shared.Reference[], compile?: boolean) : Shared.Binary[] {
        return computeTransitiveClosure([
            ...references,
            ...framework.standardReferences
        ], compile);
    }

    /**
     * Computes a transitive closure or managed binaries for the given list of references.
     * You can control whether to follow the compile or the runtime axis using the optional compile argument.
     */
    @@public
    export function computeTransitiveClosure(references: Shared.Reference[], compile?: boolean) : Shared.Binary[] {
        let results = MutableSet.empty<Shared.Binary>();
        let visitedReferences = MutableSet.empty<Shared.Reference>();

        let allReferences = [
            ...(references || []),
        ];

        for (let ref of allReferences)
        {
            computeTransitiveReferenceClosureHelper(ref, results, visitedReferences, compile);
        }

        return results.toArray();
    }

    function computeTransitiveReferenceClosureHelper(ref: Shared.Reference, results: MutableSet<Shared.Binary>, visitedReferences: MutableSet<Shared.Reference>, compile?: boolean) {
        if (visitedReferences.contains(ref))
        {
            return;
        }

        visitedReferences.add(ref);

        if (Shared.isBinary(ref))
        {
            results.add(ref);
        }
        else if (Shared.isAssembly(ref))
        {
            if (compile && ref.compile) {
                results.add(ref.compile);
            }
            if (!compile && ref.runtime) {
                results.add(ref.runtime);
            }
            if (ref.references)
            {
                for (let nestedRef of ref.references)
                {
                    computeTransitiveReferenceClosureHelper(nestedRef, results, visitedReferences, compile);
                }
            }
        }
        else if (Shared.isManagedPackage(ref))
        {
            if (compile)
            {
                results.add(...ref.compile);
            }
            else
            {
                results.add(...ref.runtime);
            }

            for (let dependency of ref.dependencies)
            {
                if (Shared.isManagedPackage(dependency))
                {
                    computeTransitiveReferenceClosureHelper(dependency, results, visitedReferences, compile);
                }
            }

        }
        else if (ref !== undefined)
        {
            Contract.fail("Unexpected reference added to this project:" + ref);
        }

        return results.toArray();
    };

    /** TODO: Unofortunately the System.Interactive.Async package exposes IAsyncEnumerable in its own System.Generic.Collection namespace,
    /*        colliding with .NETCore 3.0 implementation of the same name. For the time being (hopefully once the package maintainers updated their deps),
    /*        we explicitly create an aliased reference for the code that depends on this.
    **/

    @@public
    export function patchReferencesForSystemInteractiveAsync(references: Shared.Reference[]) : Csc.Arguments {
        const needsSystemInteractiveAsync = references.some(ref =>
            Shared.isManagedPackage(ref) && !Shared.isAssembly(ref) && ref.name.contains("System.Interactive.Async"));

        if (needsSystemInteractiveAsync) {
            return <Csc.Arguments> {
                aliasedReferences: [{
                    alias: "Async",
                    assembly: Shared.Factory.createBinary(
                        importFrom("System.Interactive.Async").pkg.contents,
                        r`lib/${tfmTargetForSystemInteractiveAsyncPackage()}/System.Interactive.Async.dll`
                    )
                }]
            };
        }

        return <Csc.Arguments> {};
    }

    function tfmTargetForSystemInteractiveAsyncPackage() : string {
        switch (qualifier.targetFramework) {
            case "net451":
                return "net45";
            case "net461":
            case "net472":
                return"net46";
            case "netcoreapp3.0":
            case "netstandard2.0":
            default:
                return "netstandard1.3";
        }
    }
}
