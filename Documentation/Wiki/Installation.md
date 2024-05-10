# Installing BuildXL

## Build Engine
Prebuilt binaries for BuildXL are only distributed internally within Microsoft. Externally you'll need to build the project locally from the main branch of its repo. See the [Developer Guide](DeveloperGuide.md) for instructions.

## DScript [Visual Studio Code](https://code.visualstudio.com) Plug-in

### Windows
You can find the plugin in the marketplace under "BuildXL". It should be recommended for our repo.

The steps to take are:

![Screenshot of VsCode with arrows for steps how to install the BuildXL extension](./InstallBuildXLToVsCode.png)

### MacOS
The plugin only contains windows binaries at the moment. For MacOS we currently only support building the plugin locally. You can build the plugin using a local copy of the BuildXL repo with:

1. `bxl out\bin\debug\ide\*`
1. Launch VsCode
1. Type 'Ctrl'+'Shift'+'P' (or choose 'View -> Command Palette...' from the main menu)
1. Type `vsix`

     ![installvsix.png](./installvsix.png)
1. Select 'Extensions: Install from VSIX
1. Navigate to: `<YourEnlistmentRoot>\Out\Bin\debug\ide` to install the extension you just built locally 
1. Select `BuildXL.vscode.osx.vsix`
1. Open a `.dsc` file to see the extension in action
1. **[macOS only]** The first time you open a `.dsc` file after installing the extension you might get the following error message
![ScreenShot2019-03-04.png](./ScreenShot2019-03-04.png)
The most likely reason for this is the `BuildXL.Ide.LanguageServer` file from the extension deployment missing Execute permission.  To fix it, locate that file in your `~/.vscode/extensions` directory and execute `chmod +x` on it, e.g.,
    ```bash
    chmod +x ~/.vscode/extensions/microsoft.buildxl.dscript-0.1.0-devBuild/bin/BuildXL.Ide.LanguageServer
    ```

## Visual Studio Plugin for opening generated solutions
This plugin enables C# and C++ target language support for building using BuildXL in Visual Studio. It is meant to be used in conjunction with the command line `bxl -vs` command that generates .g.csproj and .g.vcxproj files from DScript build specs.

### Acquire the plugin
You can build it locally with this command: `bxl out\bin\debug\ide\*`. And find the resulting file will be dropped at: `out\bin\debug\ide\BuildXL.vs.vsix`. You can install it by simply running the vsix.

If you see a message like this: `The application which this project type is based on was not found. Please try this link for further information: http://go.microsoft.com/fwlink/?LinkID=299083&projecttype=DABA23A1-650F-4EAB-AC72-A2AF90E10E37` then you need to build and install the plugin. This is the plugin's GUID which is referenced from the generated proj files from `bxl -vs`.