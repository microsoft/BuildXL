config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                f`module.config.dsc`,
                f`${Environment.getPathValue("TRANSFORMERS_SDK_DIR")}/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        {
            name: a`Out`,
            path: p`{Environment.getPathValue("OUTPUT_DIR")}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
    ]
});