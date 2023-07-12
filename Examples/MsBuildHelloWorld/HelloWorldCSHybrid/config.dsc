config({
    resolvers: [
        {
            kind: "MsBuild",
            root: d`.`,
            moduleName: "HelloWorldCS"
        },
        {
            kind: "DScript",
            modules: [{moduleName: "MyModule", projects: [f`project.dsc`]}]
        }
    ]
});