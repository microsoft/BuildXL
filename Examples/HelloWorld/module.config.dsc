module({
        name: 'HelloWorld',
        nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
        projects: [
            f`./Hello.World.Project.dsc`
        ]
});