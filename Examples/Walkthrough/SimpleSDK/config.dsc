config({
    resolvers: [
        {
            kind: "DScript",
            modules: [ 
                f`Src/HelloApp/module.config.dsc`,
                f`Src/HelloLib/module.config.dsc`,
                f`Sdk/module.config.dsc`
            ]
        },
    ],
    mounts: Context.isWindowsOS()
        ? [
            {
                name: a`MSys`,
                path: p`C:/msys64`,
                trackSourceFileChanges: true,
                isReadable: true
            },
          ]
        : []
});