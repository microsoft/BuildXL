## Building with BuildXL

The selfhost build automatically compiles the VsCode extension by defining pips for executing `npm install` and `tsc -p ./`.  It's important to note that the `npm install` pip will install only the packages explicitly specified in `cg/npm/package-lock.json` (instead of transitively fetching everything that is needed to satisfy the dependencies defined in `package.json`).  What that means is that, if you changed any package dependencies (e.g., by editing dependencies in `package.json`), they **will not** be automatically picked up by the BuildXL build; instead, you must manually run `npm install`, take the produced `package-lock.json` file, and manually overwrite the checked-in `cg/npm/package-lock.json` with it. (see [Inner Loop Development](#inner-loop-development) for more info)

To build the vsix packages with BuildXL, go to the root of the enlistment and run
```cmd
bxl Out\Bin\Debug\Ide\*
```

## Inner Loop Development

  1. Change directory into `Public/Src/IDE/VsCode/client`
  1. Fetch node packages
      ```bash
      npm install
      ```

Next:

  - To Compile TypeScript to JavaScript:
    ```bash
    npm run compile # calls `tsc -p ./` (as defined in package.json)
    ```

  - To debug the extension with VsCode: 
    - Open the `Public/Src/IDE/VsCode/client` folder in VsCode
    - Choose `Debug` -> `Start Debugging` from VsCode

  - (optional) To Create .vsix package (note that this alone won't include DScrip language server binaries):
    - make sure you have `vsce` installed; if you don't, you can install it globally
      ```bash
      npm install -g vsce
      ```
    - to create a VsCode vsix package, run
      ```bash
      npm run package # calls `vsce package` (as defined in package.json)
      ```

**[Important] Before returning to building with BuildXL**: 
  - go to `Public/Src/IDE/VsCode/client` (which was likely your PWD the whole time)
  - move `package-lock.json` to `<enlistment-root>/cg/npm/package-lock.json`
  - delete the `package-lock.json` file
  - delete the `node_modules` folder
  - delete the `out` folder

In bash, that looks like
```bash
mv -v package-lock.json ../../../../../cg/npm/package-lock.json
rm -rf node_modules/ out/
```