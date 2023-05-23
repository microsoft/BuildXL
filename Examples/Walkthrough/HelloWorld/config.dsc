config({
    resolvers: [
        {
            kind: "DScript",
            modules: [ f`module.config.dsc` ]
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