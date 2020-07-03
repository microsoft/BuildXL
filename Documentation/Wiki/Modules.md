In BuildXL, Modules represent a group of Projects which exposes a set of public (tagged with `@@public` values. Modules can include references to other Module, Project and List files. The default top-level module file is named "module.config.bm"

A module can import values from another module using `importFrom` or `import` (see [Import and Exports](./DScript/Import-export)).

## Module size guidelines
How big should a module be? A module is intended to be roughly equivalent to a solution file: it contains closely related code and its validation. This could mean that one module contains anything from one team's code to an entire (small) product. 

There aren't any hard requirements for module size, but consider the following:
* If multiple modules have a dependency on a small subset of a module, then consider splitting that subset into its own module. This can free BuildXL to build the subset faster and unblock its dependents sooner.

A set of module-level policies can be specified in order to constrain module dependencies.

## File Format
Module configuration files are regular DScript files except that they may only contain one or more top-level calls to the predefined `module` function.  

The following is the simplest possible module configuration, which defines a single module named "ModuleA":
```ts
// comments are allowed
module({ name: "ModuleA" });
```

When no `projects` are explicitly specified in a module configuration (like in the example above), all reachable DScript files are automatically discovered and added to the module being defined.  Assuming the following directory layout
```
RootDirectoryOfModuleA
├── module.config.bm
├── Project1
│   └── project.bp
└── Project2
    └── project.bp
```
and the content of the `module.config.bm` file as listed above, the result is a module named "ModuleA" containing projects `Project1\project.bp` and `Project2\project.bp`.

Attempting to add anything other than a call to `module`, as in
```ts
const foo = 42; // declarations, however, are not allowed
module({ name: "ModuleA" });
```
fails with `error DX9223: Unexpected statement in module configuration file`.

Multiple module declaration may exist in a single module configuration file.  In such a case, however, each module declaration **must** explicitly specify its projects. Furthermore, the sets of specified projects must be mutually disjoint.  For example, assuming the same directory layout, the following content of `module.config.bm` correctly defines 2 modules
```ts
module({ name: "ModuleA1", projects: [ f`Project1/project.bp` ] });
module({ name: "ModuleA2", projects: [ f`Project2/project.bp` ] });
```
while this
```ts
module({ name: "ModuleA1" });
module({ name: "ModuleA2" });
```
results in `error DX9313 [...] When defining multiple packages in a single package configuration file, none of them may implicitly own all projects (by omitting to specify the 'projects' field)`.

For completeness, below is a full type definition of the `module` function and its argument, which details all supported options for module configuration

```ts
/** Ambient function that is used for module configuration. */
declare function module(module: ModuleDescription): void;

/** Module description that could be used to configure a module */
interface ModuleDescription {
    /** Name of the package */
    name: string;
    
    /** Version of the package */
    version?: string;
    
    /** Package publisher */
    publisher?: string;
    
    /** Projects that are owned by this package. */
    projects?: (Path|File)[];
    
    /** Whether this package follows the legacy (deprecated) semantics or DScript V2 semantics for module resolution*/
    nameResolutionSemantics?: NameResolutionSemantics;
    
    /** Modules that are allowed as dependencies of this module. If this field is omitted, any module is allowed.*/
    allowedDependencies?: ModuleReference[];
    
    /** Dependent modules that are allowed to be part of a module-to-module dependency cycle. If omitted, cycles are not allowed. */
    cyclicalFriendModules?: ModuleReference[];
}
```
## Example
These files are typically named `module.config.bm`. They declare the contents and the policies of modules. 

A typical module configuration file looks similar to the following:

```typescript
// file:  Contoso/Fabrikam/NodPublishers/module.config.bm

module({
    name: "Contoso.Fabrikam.NodPublishers",
    projects: [
        ...importFile(f`Desktop\Client\Client.bl`).projects,
        ...importFile(f`Desktop\Helpers\Helpers.bl`).projects,
        ...importFile(f`Desktop\Service\Service.bl`).projects,
        // etc etc
     ],
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
});
```

The `name` is a globally unique name. You should think of this name as your [nuget package name](https://docs.microsoft.com/en-us/nuget/quickstart/create-and-publish-a-package) or [Maven package name](https://maven.apache.org/guides/mini/guide-naming-conventions.html).

We recommend using a hierarchical naming scheme, starting with a globally unique prefix, such as your company name followed by product and component. This makes your module portable and consumable by others.

Modules can be nested. When nesting modules, it is recommended that a child module shares the same prefix as the parent module.

The `projects` field requires an array of project files. The above examples consumes [list files](./DScript/List-files.md), but you can use [globbing](./DScript/Globbing.md) here as well:

```typescript
// file:  Contoso/Fabrikam/NodPublishers/module.config.bm

module({
    name: "Contoso.Fabrikam.NodPublishers"
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: globR(d`.`, "*.bp"),
});    
```
You can leverage list files here as well. See [List](/BuildXL/User-Guide/Script/List) for details on this.

The `nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences` is something temporary. That line states that this module uses the latest version ("V2") of build specifications, and not a previous implementation that we can't remove due to backwards compatibility requirements. We are actively working on removing that requirement so that all DScript will run with V2 enabled by default.
