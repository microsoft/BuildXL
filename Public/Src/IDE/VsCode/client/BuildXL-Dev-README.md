## Building with BuildXL

The selfhost build automatically compiles the VsCode extension by defining pips for executing `npm install` and `tsc -p ./`.  It's important to note that the `npm install` pip will install only the packages explicitly specified in `package-lock.json` (instead of transitively fetching everything that is needed to satisfy the dependencies defined in `package.json`).  What that means is that, if you changed any package dependencies (e.g., by editing dependencies in `package.json`), they **will not** be automatically picked up by the BuildXL build; instead, the build will fail with a "double write" DFA on `package-lock.json`.  To resolve this, manually run `npm install` to update the `package-lock.json` file in place. (see [Inner Loop Development](#inner-loop-development) for more info)

To build the vsix packages with BuildXL, go to the root of the enlistment and run
```cmd
bxl Out\Bin\Debug\Ide\*
```

## Inner Loop Development

  1. Change directory into `Public/Src/IDE/VsCode/client`
  1. Fetch node packages
      ```
      npm install
      ```

Next:

  - To Compile TypeScript to JavaScript:
    ```
    npm run compile # calls `tsc -p ./` (as defined in package.json)
    ```

  - To debug the extension with VsCode: 
    - Open the `Public/Src/IDE/VsCode/client` folder in VsCode
    - Choose `Debug` -> `Start Debugging` from VsCode

  - (optional) To Create .vsix package (note that this alone won't include DScript language server binaries):
    - make sure you have `vsce` installed; if you don't, you can install it globally
      ```
      npm install -g vsce
      ```
    - to create a VsCode vsix package, run
      ```
      npm run package # calls `vsce package` (as defined in package.json)
      ```

**[Important] Before returning to building with BuildXL**: 
  - delete the `node_modules` folder
  - delete the `out` folder

If those folders are not manually deleted, the build is likely to result in "double write" errors because both the "copy directory" pip (copying the `client` folder to an output directory), and the "npm install" pip will produce the content under `node_modules`.