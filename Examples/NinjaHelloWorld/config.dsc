config({
    resolvers: [
        {
            kind: "Ninja",
            root: d`src`,
            moduleName: "HelloWorld",
            keepProjectGraphFile: true,
        }
    ]
});