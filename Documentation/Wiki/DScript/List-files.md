List files are used to offload definitions of lists Configuration Module Project, and other files.  Hence, a list file typically exports one or more arrays of files.

They address the problems that commonly arise from having to maintain a large list of items specified in a single file when the file is likely to be edited concurrently by multiple people.  In such a scenario, merge conflicts happen often and are tedious to resolve.  Keeping the file sorted somewhat alleviates the problem, but does not solve it entirely.  In BuildXL, using globbing with wildcards can remedy the problem when the desired list can be expressed using the supported globbing patterns; in practice, however, that is not always possible, especially when the list of files may be conditioned upon environment variables or other inputs.

List files, like regular DScript files, may import one another, which makes it possible to recursively construct one large final list from an arbitrary number of small list files.

# Configuration example

 We can move that list of file literals from the config file into a separate list file. This helps to clean up the config file:

```ts
// file: config.bc
config({
    resolvers: [
        {
            kind: "SourceResolver",
            modules: importFile(f`myBuildList.bl`).modules
        }
    ]
});
```
With the file literals in `myBuildList.bl`, they no longer get in the way of the build logic of `config.bc`:
```ts
// file: myBuildList.bl
export const modules = [
    f`NodPublishers/module.config.bm`,
    f`ReleCloud/module.config.bm`,
    f`WingtipToys/module.config.bm`,
];
```

This process can then repeat as many times as necessary, each time breaking up one large list file into a number of smaller ones, e.g.:
```ts
// file: myBuildList.bl
export const modules = [
    f`NodPublishers/module.config.bm`,
    ...importFile(f`myNewBuildList.bl`).modules
];
```

```ts
// file: myNewBuildList.bl
export const modules = [
    f`ReleCloud/module.config.bm`,
    f`WingtipToys/module.config.bm`,
];
```

# Module configuration example

Here we refactor a module file to use list files and nicely follow the directory structure

```ts
// file:  Contoso/Fabrikam/NodPublishers/module.config.bm
module({
    name: "Contoso.Fabrikam.NodPublishers"
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: importFile(f`dirs.bl`).projects
});

// file:  Contoso/Fabrikam/NodPublishers/dirs.bl
export const projects = [
    ...importFile(f`Desktop/dirs.bl`).projects
    ...importFile(f`Web/dirs.bl`).projects
];

// file:  Contoso/Fabrikam/NodPublishers/Desktop/dirs.bl
export const projects = [
    f`Helpers/Helpers.bp`,
    ...importFile(f`Client/dirs.bl`).projects,
    ...importFile(f`Service/dirs.bl`).projects,
];

// file:  Contoso/Fabrikam/NodPublishers/Desktop/Client/dirs.bl
export const projects = [
    f`App/App.bp`,
    f`Controls/Controls.bp`,
    f`TreyResearch/TreyResearch.bp`,
];

// file:  Contoso/Fabrikam/NodPublishers/Desktop/Service/dirs.bl
export const projects = [
    f`Shared/Shared.bp`,
    f`Unix/Unix.bp`,
    f`Windows/Windows.bp`,
];

// file:  Contoso/Fabrikam/NodPublishers/Web/dirs.bl
export const projects = [
    f`Api/Api.bp`,
    f`Controllers/Controllers.bp`,
    f`Controls/Controls.bp`,
];
```

It is up to you to define how deep to go with list files. 
* You can put out guidelines to always follow the entire directory structure, or
* You can put out guidelines to stop at a certain number of projects.

The BuildXL engine does not dictate any specific pattern and we encourage our users to use the notation that makes the most sense for their own scale of operation.

