# Customizing package script commands
When BuildXL interacts with well-known coordinators, the available script commands on each package are either provided by the coordinator object model (the case of Rush, for example) or BuildXL directly loads the 'scripts' section of the corresponding `package.json` file.

However, there are cases where scripts commands need to be customized or there are actually no `package.json` files physically present. The latter is a scenario which is not uncommon when [building beyond well-known monorepo managers](js-custom-graph.md).

A callback can be defined when configuring a JavaScript resolver to provide scripts commands for a given package. The result of the callback will override any existing `package.json` file that was found for the given package. We use Rush in the example below, but any JavaScript-based resolver supports this option.

```typescript
config({
    resolvers: [
        {
            kind: "Rush",
            moduleName: "my-repo",
            ...
            customScripts: importFile(f`custom-scripts.dsc`).getCustomScripts
        }
});
```
The callback can be inlined as part of the main `config.dsc` file, but for the sake of clarity the recommendation is to define it in a different file. The callback is provided with the package name and location relative to the repo root for each package present in the build graph. 

```typescript
// custom-scripts.dsc
export function getCustomScripts(packageName: string, location: RelativePath): File | Map<string, FileContent> {
    
    switch (packageName){
        case "@ms/custom-build": {
            return Map.empty<string, FileContent>()
                .add("build", "npm run custom-build");
                .add("test", ["npm run jest -- ", location]);
        }
        case "@ms/custom-test" : {
            return f`path/to/custom/location/package.json`
        }
        default: {
            return undefined;
        }
    }
}
```

In this case, the callback is customizing the scripts of two packages, `@ms/custom-build` and `@ms/custom-test`. For `@ms/custom-build`, it is returning two script commands, `build` and `test`. For `@ms/custom-test` it is returning a `package.json` file from an arbitrary location. In this case BuildXL will look at the 'scripts' section of that file and associate the scripts defined there to `@ms/custom-test` package. Finally, for all other cases the callback returns `undefined`. The value `undefined` represents the callback does not want to provide customizations for a given package.