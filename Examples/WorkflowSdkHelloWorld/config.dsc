config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                {
                    moduleName: 'HelloWorld',   
                    projects: [f`./Hello.World.Project.dsc`]
                }
            ]
        },
    ],
    mounts: [
        {
            name: a`Out`,
            path: p`outputs`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
    ]
});