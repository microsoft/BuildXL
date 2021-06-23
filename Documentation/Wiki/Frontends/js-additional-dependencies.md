# Specifying additional dependencies
Pip dependencies scheduled via a JavaScript resolver are inferred from the package-to-package dependencies specified by the corresponding `package.json` files (or user provided, in the case of [building beyond well-known monorepo managers](js-custom-graph.md)). This is usually enough for most cases, since the dependency information available for the corresponding underlying JavaScript coordinator is sufficient information for BuildXL as well.

However, there are scenarios that require extra dependencies to be defined. A typical example is the case where other resolvers are involved in a build (not necessarily JavaScript ones, even thought that's also an option) and there are values exported from those resolvers that we need our JavaScript projects to depend on. Let's consider an example where the `Download` resolver is used to download `git` during the build, and we have a JS project that depends on `git` being available. This is usually not something that needs to be reflected in a local build, where installing `git` may be presented as a user pre-build requirement. But for a lab build where tools need to be precisely pinned, doing this in-build is very convenient. Check section [Package installation under BuildXL](js-package-install.md) for additional examples.

The `Download` resolver configuration may look like:

```typescript
config({
    resolvers: [{
        kind: "Download",
        downloads: [
            {
                moduleName: "Git",
                extractedValueName: "gitPackage",
                url: 'https://github.com/git-for-windows/git/releases/download/v2.30.1.windows.1/MinGit-2.30.1-64-bit.zip',
                archiveType: "zip"
            }
        ]}
    ]
}
```
This resolver will download `git`, unzip it and expose it as the value `gitPackage` in module `Git` for other resolvers to consume. Let's assume now that there is a project `@ms/source-control` that depends on `git` being installed. Now let's connect `@ms/source-control` with `git`:

```typescript
config({
    resolvers: [
        {
            kind: "Download",
            // ...
        }, 
        {
            kind: "Rush",
            moduleName: "rush-test",
            root: d`.`,
            execute: ['build', 'test'],
            additionalDependencies: [{
                dependencies:[ {expression: "importFrom('Git').gitPackage"} ],
                dependents:['@ms/source-control'] 
            }]
        }
    ]
}
```
A collection of additional dependencies can be added, where each dependencies/dependents pair will define all values captured by `dependencies` to depend on all values captured by `dependents`. In this case, the pip that represents downloading and unpacking `git` will become a dependency of `@ms/source-control`.

Dependencies can select arbitrary files and static directories as well as JavaScript projects this resolver owns. Dependents are always owned JavaScript projects:

```typescript
interface JavaScriptDependency {
    dependencies: (JavaScriptProjectSelector | LazyEval<File> | LazyEval<StaticDirectory>)[], 
    dependents: JavaScriptProjectSelector[]
}
```

A `JavaScriptProjectSelector` allows for simple strings (as in the example above) as well as regular expressions to capture particular subsets of JavaScript projects. Check the [resolver configuration settings](Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc) for the full definition.

Adding extra dependencies across JavaScript projects is usually not the recommended approach. Consider the most natural way to do this is is the corresponding `package.json` files, the source of truth for package dependencies. The ability to declare additional dependencies is recommended when files or directories are defined in other resolvers. However, there are cases where undeclared dependencies are observed at runtime and adding a dependency in `package.json` files is not possible (e.g. because the package is a third party one and therefore not easy to modify). 

For example, consider a case where the test script of a project is reading from a third party package. Assume these reads are not expected and are producing file monitoring violations. In this case, we could temporary add the extra dependency until we can figure out why those are happening:

```typescript
config({
    resolvers: [
        {
            kind: "Rush",
            moduleName: "rush-test",
            root: d`.`,
            execute: ['build', 'test'],
            additionalDependencies: [{
                dependencies:[{packageName: '@ms/third-party'}],
                dependents:[{packageName: '@ms/my-package', Commands: ['test']] 
            }]
        }
    ]
}
```
Here the script command `test` from `@ms/my-package` will now have a dependency on `@ms/third-party` (on all its script commands).