A configuration file represents the root of a BuildXL build. As such, it defines where to find the roots for each module, how to consume source files, and assorted fine-tuning settings for several build stages. Similar to [Modules](./Modules.md), they contain only one function call. Where module files call `module`, configuration files call `config`.

A configuration file may be as simple as:

```ts
config({ modules: [ f`module.config.bm` ] });
```

This informs BuildXL that the build consists of a single module defined in `module.config.bm`.

# Configuration Settings

Described next are the most useful configuration settings (the rest of the fields provided by the `Configuration` interface should be self-explanatory):
* `qualifiers`
  * [Qualifier Configuration](#Qualifiers) configuration, including qualifier space, default qualifier, and named qualifiers.
* `modules`
  * A list of [module definitions](./Modules.md) comprising the build
* `projects`
  * A list of orphan projects, i.e. projects not belonging to any module.
* `resolvers`
  * A list of [resolver settings](#Resolvers) whose purpose is to resolve modules by name (i.e., given a symbolic module name find a physical location of the module).
* `disableDefaultSourceResolver`
  * Instead of specifying `modules` and `projects` explicitly, the default source resolver (enabled by default) can discover them automatically.  This field can be used to prevent that behavior, because, in general, it is recommended that `modules` and `projects` be specified explicitly.
* `mounts`
  * An arbitrary list of [mount points](Advanced-Features/Mounts.md). A mount point assigns a symbolic name and read/write permissions to a directory location.

```ts
interface Configuration {
    qualifiers?: QualifierConfiguration;

    /** Set of projects in the build cone. */
    projects?: File[];

    /** Set of source modules for current build cone. */
    modules?: File[];

    /** Set of special objects that are used to resolve actual physical location of the module. */
    resolvers?: Resolver[];

    /** The environment variables that are accessible in the build. */
    allowedEnvironmentVariables?: string[];

    /** Mount points that are defined in the build */
    mounts?: Mount[];

    /** Disable default source resolver. */
    disableDefaultSourceResolver?: boolean;

    /**
     * List of file accesses that are benign and allow the pip that caused them to be cached.
     *
     * This is a separate list from the above, rather than a bool field on the exceptions, because
     * that makes it easier for a central build team to control the contents of the (relatively dangerous)
     * cacheable allowlist.  It can be placed in a separate file in a locked-down area in source control,
     * even while exposing the (safer) do-not-cache-but-also-do-not-error allowlist to users.
     */
    cacheableFileAccessAllowlist?: FileAccessAllowlistEntry[];

    /** List of file access exception rules. */
    fileAccessAllowList?: FileAccessAllowlistEntry[];

    /** 
     *  Reclassification rules that will be applied to all pips in the build.
     *  These rules are traversed in order and the first matching rule is applied.
     *  Rules defined here are checked only after the rules defined for a pip individually.
     */
    globalReclassificationRules?: ReclassificationRule[];


    /** List of rules for the directory membership fingerprinter to use */
    directoryMembershipFingerprinterRules?: DirectoryMembershipFingerprinterRule[];

    /** Overrides for the dependency violations */
    dependencyViolationErrors?: DependencyViolationErrors;

    /** Configuration for front end. */
    frontEnd?: FrontEndConfiguration;
    
    /** BuildXL engine configuration. */
    engine?: EngineConfiguration;
}
```
## Qualifiers
The concept of qualifiers is explained [here](DScript/Qualifiers.md), where sample usages can be found too.  This section only goes over the `QualifierConfiguration` interface (and related abstractions) used to configure them.

A qualifier configuration consists of three parts:
* `qualifierSpace`: a collection of key-value pairs, where a key is a string (think of it as "a name of a build parameter") and a value is a collection of strings (think of it as "allowed values of the corresponding build parameter").
* `defaultQualifier`: a collection of string-string key-value pairs which specifies a default value for every build parameter defined in `qualifierSpace`.
* `namedQualifiers`: allows the user to define symbolic names for concrete qualifier instances (those symbolic names can then be specified via the command line).

```ts
interface QualifierInstance {
    [name: string]: string;
}

interface QualifierSpace {
    [name: string]: string[];
}

interface QualifierConfiguration {
    /** The default qualifier space for this build */
    qualifierSpace?: QualifierSpace;

    /** The default qualifier to use when none specified on the commandline */
    defaultQualifier?: QualifierInstance;

    /** A list of alias for qualifiers that can be used on the commandline */
    namedQualifiers?: {
        [name: string]: QualifierInstance;
    };
}
```
## Resolvers
Resolvers are used to resolve a symbolic module name to a physical module configuration file.

Modules specified explicitly (via the `modules` field in the main configuration) don't require a special resolver.  The general recommendation is that the modules defined as part of the build should be specified explicitly; in that case, resolvers should be used only to point to any DScript SDKs and/or NuGet packages needed for the build.

BuildXL supports different types of resolvers for different kinds of build projects ([`KnownResolverKind.cs`](../../Public/Src/FrontEnd/Sdk/Workspaces/Utilities/KnownResolverKind.cs)). For example, the `DScript` resolver can find modules whose sources are already present on disk, while `NuGetResolver` takes a list of NuGet packages which it downloads and treats as DScript modules.

A typical way to include DScript SDKs in a build is to define a `Dscript` resolver like the following:
```ts
{
    kind: "DScript",
    modules: [
        ...globR(d`MySDKs`, "module.config.bm"),
    ]
}
```

To add NuGet packages, a `NuGetResolver` like the following should be added to the main configuration:
```ts
{
    kind: "Nuget",

    repositories: {
        "nuget": "https://api.nuget.org/v3/index.json",
        "myget-dotnet.core": "https://www.myget.org/F/dotnet-core/api/v3/index.json"
    },

    packages: [
        { id: "Microsoft.NetCore.Analyzers", version: "2.3.0-beta1" },
        { id: "System.Runtime", version: "4.3.0" },
        { id: "System.ValueTuple", version: "4.3.0" },
        // ...
    ]
}
```

For completeness, below are type definitions of both `SourceResolver` and `NuGetResolver`.
```ts
interface DScriptResolver extends ResolverBase {
    kind: "DScript";

    /** Root directory where packages are stored. */
    root?: Directory;

    /** List of modules with respecting path where to look for this module or its inlined version. */
    modules?: (File | InlineModuleDefinition)[];

    /** Whether specs under this resolver's root should be evaluated as part of the build. */
    definesBuildExtent?: boolean;
}

interface NuGetResolver {
    kind: "Nuget";   
    
    /** Optional configuration to fix the version of nuget to use.  When not specified the latest one will be used. */
    configuration?: NuGetConfiguration;
    
    /** List of Nuget repositories. Keys are arbitrary names, values are URLs. */
    repositories?: { [name: string]: string; };  
    
    /** The transitive set of NuGet packages to retrieve (must be closed under dependencies). */
    packages?: {id: string; version: string; alias?: string; tfm?: string; dependentPackageIdsToSkip?: string[]}[];
}
``` 

## File access allowlists
It is best to specify all file accesses. This way BuildXL tracks those files can can provide correct caching. On rare occasion some files may need to be untracked. For example when a process consumes system files that are known to be inconsequential to the build and there may be some variability in the content of those files across machines which would prevent cross machine caching.

On an even rarer occasion, it may not be possible to predict the path of files that you desire to untrack. They may be nondeterministic. Allowlists exist for this last resort. **Use of allowlists is untracked and unsafe. They should be reserved as a last resort.**  

For information on how to specify allowlists [click here](./Advanced-Features/Observation-Reclassification.md#file-access-allowlists) 

# Example
When deciding how to organize a build (and all of its modules and projects), the most relevant configuration fields are `modules` and `resolvers`.  In this example, let's assume the build consists of 3 modules, defined in files `NodPublishers/module.config.bm`, `ReleCloud/module.config.bm`, and `WingtipToys/module.config.bm`.  To be included in a BuildXL build, they should be listed under the `modules` field:
```ts
config({
    modules: [ f`NodPublishers/module.config.bm`, f`ReleCloud/module.config.bm`, f`WingtipToys/module.config.bm` ]
});
```

While the `resolvers` field is typically used to specify DScript SDKs and NuGet packages, it may be used for specifying build modules too, e.g.,
```ts
config({
    resolvers: [
        {
            kind: "SourceResolver",
            modules: [ f`NodPublishers/module.config.bm`, f`ReleCloud/module.config.bm`, f`WingtipToys/module.config.bm` ]
        }
    ]
});
```

To include some DScript SDKs and NuGet packages in your build, add a `SourceResolver` and a `NuGetResolver`, respectively:
```ts
config({
    modules: [
        f`NodPublishers/module.config.bm`, 
        f`ReleCloud/module.config.bm`,
        f`WingtipToys/module.config.bm`
    ],
    resolvers: [
        {
            kind: "SourceResolver",
            modules: globR(d`Sdk`, "module.config.bm")
        },
        {
            kind: "Nuget",
            repositories: {
                "nuget": "https://api.nuget.org/v3/index.json",
                "myget-dotnet.core": "https://www.myget.org/F/dotnet-core/api/v3/index.json"
            },
            packages: [
                { id: "Microsoft.NetCore.Analyzers", version: "2.3.0-beta1" },
                { id: "System.Runtime", version: "4.3.0" },
                { id: "System.ValueTuple", version: "4.3.0" },
                // ...
            ]
        }
    ]
});
```

In the listing above, instead of explicitly listing all SDK modules found under the `Sdk` directory, we used [globbing](./DScript/Globbing.md), i.e., the `globR` function to recursively search the `Sdk` directory for all files named `module.config.bm` (in general, the search pattern may include wildcards).

When the list of modules becomes unmanageably large for a single file, consider using [List Files](./DScript/List-files.md):

```typescript
// file:  config.bc
config({
    modules: importFile(f`modules.bl`).modules,
})
```
```ts
// file:  modules.bl
export const modules = [
    f`NodPublishers/module.config.bm`,
    f`ReleCloud/module.config.bm`,
    f`WingtipToys/module.config.bm`,
];
```