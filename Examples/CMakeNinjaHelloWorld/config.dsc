config({
    resolvers: [
        {
            kind: "Ninja",
            root: d`.`,
            moduleName: "CMakeNinjaProject",
        }
    ],
    
    // Inbox SDK is not currently working with the CMake resolver
    disableInBoxSdkSourceResolver: true,
});