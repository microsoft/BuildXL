config({
    resolvers: [
        {
            kind: "Ninja",
            root: d`src`,
            moduleName: "HelloWorld",
            keepProjectGraphFile: true,
        }
    ],
    
    // Inbox SDK is not currently working with the Ninja resolver
    disableInBoxSdkSourceResolver: true,
});