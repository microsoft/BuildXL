config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                { moduleName: "ComposedGraph", projects: [f`./composedGraph.dsc`] },
                f`${Environment.getPathValue("BUILDXL_BIN")}/Sdk/Sdk.Transformers/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        {
            // Mounts defined in graph fragments have no bearing on a build that consumes them.
            // The build must define all necessary mounts.  
            name: a`Out`,
            path: p`${Environment.getPathValue("OUTPUT_DIR")}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
        {
            name: a`GraphFragments`,
            path: p`${Environment.getPathValue("GRAPH_FRAGMENTS_LOCATION")}`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        }
    ]
});