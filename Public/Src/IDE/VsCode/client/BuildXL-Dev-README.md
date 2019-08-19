## For inner loop development

  1. Open this folder in VsCode
  2. Fetch node packages
```bash
npm install
```
Next:

  - To Compile TypeScript to JavaScript:
```bash
npm run compile # calls `tsc -p ./` (as defined in package.json)
```
  - To Create .vsix package (note that this alone won't include DScrip language server binaries):
```bash
npm run package # calls `vsce package` (as defined in package.json)
```
  - To debug the extension with VsCode: 
    - Choose `Debug` -> `Start Debugging` from VsCode

## When building with BuildXL

The current selfhost build **DOES NOT** automatically
  1. fetch node packages (`npm install`), nor does it
  2. compile TypeScript to JavaScript (`tsc -p ./`).

Instead, the build expects the output folder of those two operations (`./node_modules` and `./out` respectively) to be manually xcopy-ed over to
`../pluginTemplate/extension`.  The selfhost build will to the rest automatically, namely:
  - bulid the DScript language server
  - deploy the DScript language server binaries into the extension's `bin` folder
  - create `vsix` packages for both Windows and Mac.

In summary, before running the selfhost build, make sure to do the following:
```bash
# update the content of ./node_modules
npm install

# update the content of ./out
npm run compile

# sync the content of ./node_modules and ./out
rsync -raz node_modules out ../pluginTemplate/extension/ --delete
```

To then build the vsix packages with BuildXL, go to the root of the enlistment and run
```cmd
bxl Out\Bin\Debug\Ide\*
```