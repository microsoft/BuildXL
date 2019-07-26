# BuildXL DScript IDE

## Making a change
In order to avoid doing TS compilations in-build (that would have to deal with installing packages, etc.) the 'pluginTemplate' folder contains a checked-in compiled version extension.ts. 

- This means that any changes to files under
 <b>client/src</b> needs to be recompiled and the checked-in file updated.
The recompilation can be triggered by manually running <b>buildVsix.bat</b>, and then replacing <b>pluginTemplate/extension/out/src/extension.ts</b> with the result of the compilation, located under client/out/src.

- The same goes for node_modules, some of which are checked in. If you update <b>client/package.json</b> with a new version of a node module, please make sure to check in the updated modules to <b>pluginTemplates/extension</b>


## Debugging with VSCode
Step by step:
> Install NPM (https://www.npmjs.com/get-npm)

> code Public\Src\IDE\VsCode\client

> Hit F5
(it opens Extension Development Window, where you can open folder, open .dsc files etc). No need to ever install vsix for debugging

- Note: VSCode should install all the node modules that you need.. but if not you can just do "npm install" in the client folder (where the package.json is)

> In the Extension Development Window, go to any DScript folder or project, and party!
