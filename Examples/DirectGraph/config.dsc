// Example that exercises the DirectGraph resolver.
// GraphEmitter is a .NET project: `dotnet run` builds it, and it reads graph.json and streams the corresponding pip
// operations to BuildXL. Paths in graph.json are relative to the resolver's working directory, so this example runs from
// any enlistment.
config(
  {
    resolvers: [
      {
        kind: "DirectGraph",
        workingDirectory: d`.`,
        moduleNames: [a`test`],
        tool: f`${Environment.getPathValue("ProgramFiles")}/dotnet/dotnet.exe`,
        arguments: ["run", "--project", "GraphEmitter", "graph.json"],
        enableProjectGraphVerboseLogging: true,
      },
      {
        kind: "DScript",
        modules: [{ moduleName: "consumer", projects: [f`consumer.dsc`] }],
      },
    ],
    mounts: [
      {
            name: a`out`,
            path: p`out`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: false,
        },
    ]
  });