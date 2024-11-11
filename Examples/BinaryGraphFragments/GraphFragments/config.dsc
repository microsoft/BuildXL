config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                { moduleName: "GraphFragments", projects: [f`./fragments.dsc`] },
                f`${Environment.getPathValue("BUILDXL_BIN")}/Sdk/Sdk.Transformers/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        {
            name: a`Out`,
            path: p`Out\Bin`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
        {
            name: a`fragments`,
            path: p`fragments`,
            trackSourceFileChanges: true,
            isReadable: true,
            isWritable: false
        }
    ]
});