# Installating packages under BuildXL
Most package managers offer an 'install' (or equivalent) verb to download and install all package external dependencies. Some will also prepare local packages to be able to access local package references, usually by placing symlinks in the right places. Even though package installation is usually exposed as a single monolithic block of execution, which means it is not very amenable to caching and distribution, running package installation as a BuildXL build can still benefit from caching. Observe that as long as the package 'lock' file doesn't change, which usually means a change in the package dependencies, getting a cache hit is possible, which saves download and installation time.

The recommended approach to achieve this is to run a BuildXL build before the 'real' build. This is usually referred as a 'prep' build. This build will execute the package install step (plus maybe some other custom steps the particular repo needs) so the main build can be executed right after consuming the result of the installation.

Local builds can sometimes assume pre-installed tools. However, that's usually not the case for lab builds. The 'prep' build is a convenient step where all required tools can be downloaded. This can be achieved with a 'Download' resolver that allows for this to happen under BuildXL.

Let's set up a 'prep' build by creating a separate `config.dsc` file (different from the main one we need to create for the main build). This file can be anywhere on disk. In this example, let's assume there is an `install` folder for it: 

```javascript
// install/config.dsc
resolvers: [
        {
            kind: "Download",
            downloads: [
                {
                    moduleName: "nodeTool",
                    extractedValueName: "nodePackage",
                    url: 'https://nodejs.org/dist/v14.15.4/node-v14.15.4-win-x64.zip',
                    archiveType: "zip"
                },
                {
                    moduleName: "yarnTool",
                    extractedValueName: "yarnPackage",
                    url: 'https://registry.npmjs.org/yarn/-/yarn-1.22.4.tgz',
                    archiveType: "tgz"
                }
            ]
        },
```

In the example above we are instructing the download resolver to download node and yarn tools. By specifying the extracted value name and the archive type, the download resolver will unpack the download result for us and expose the content under the given names for other resolvers to consume.

Let's say that our repo needs to run 'yarn install'. We can then create a `DScript` resolver and use one of the available SDKs:

```javascript
// install/config.dsc
resolvers: [
        {
            kind: "Download",
            ...
        },
        {
            kind: "DScript",
            modules: [{moduleName: "install", projects: [f`package-install.dsc`]}]
        },
```

```javascript
// install/package-install.dsc
import {Node, Yarn} from "Sdk.JavaScript";
import {nodePackage} from "nodeTool";
import {yarnPackage} from "yarnTool";

const install = Yarn.runYarnInstall({
        yarnTool: Yarn.getYarnTool(yarnPackage),
        nodeTool: Node.getNodeTool(nodePackage),
        // this assumes the root of the repo is one level up wrt 'install' folder
        repoRoot: d`${Context.getSpecFileDirectory().parent}`, 
    });
```
Observe that `nodeTool` and `yarnTool` are the module names associated with the downloaded content, as specified in the Download resolver settings. The extracted value names (`nodePackage` and `yarnPackage`) represent [static directories](../DScript/Types.md) that  `runYarnInstall` function can directly consume. The module `Sdk.JavaScript` is one of the in-box SDKs automatically included with BuildXL binaries.

## Running a package install build
There are some special considerations when running a package install build. These are related to the fact that the product build is going to run right after it:
* By default, BuildXL output files are hardlinks pointing to the local BuildXL cache. This is done mainly for performance reasons. These files are deny-ACLed to prevent any external modification, since the cache is not meant to be modified by the user directly. However if a product build will follow the 'prep' one, some of those outputs may be attempted to be written. If that's the case for your repo, the easiest option is just to disable output hardlinking, so outputs become just regular files with no special ACLs. The option `/hardlinks-` has to be passed to BuildXL in order to accomplish this.
* In order to ensure build determinism, BuildXL will automatically try to remove stale outputs from older builds before the build starts. But in the case of a 'prep' build, that's exactly what we want. The option `/unsafe_SkipFlaggingSharedOpaqueOutputs` has to be passed so outputs produced by the 'prep' build will be spared when running the product build.