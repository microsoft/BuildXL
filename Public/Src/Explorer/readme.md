# Build Explorer

## Contribute
The Build explorer at the moment is not the default viewer experience.
You will have to do some manual steps to develop features until it is part of the main build.

### OneTime machine setup
1. Install VS2017 with DotNetCore development.
1. Install Node (nodejs.org)
1. Install yarn globally: `npm install -g yarn`
1. Install electron-forge globally: `npm install -g electron-forge`

### Each time you sync
1. Restore npm packages from `public/src/explorer/app` run `yarn install`. DO NOT use npm to do this.
1. Build BuildXL with dotnet core. Run `bxl /q:DebugDotNetCore` from the root of the enlistment

### Setup devloop
1. Dev the server part
    1. Open the project file `public/src/explorer/server/bxp-server.csproj` in VS2017
    1. F5 to run.
1. Dev the client app
    1. Open the project in vscode for the best editting experience with autocomplete
    1. In the vscode terminal go to directory: `public/src/explorer/app` and lauch `electron-force start`.
    1. Go to the settings in the app, select `use devserver`. The defaults should match the csproj file, but you can tweak if you have it setup differently.

### Dev-Tricks
1. In the electron-app on the Debug menu allows you to enable the F12 developer tools. Or you can use F12.
1. In the electron-app on the Debug menu there is a refresh page to pick up the changes.
1. In the server app you can simply restart the server when you make changes.