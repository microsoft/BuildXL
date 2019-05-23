
/** Returns a list of nuget packages required to use Task/Task<T>/ValueTask<T>. */
@@public
export const tplPackages = isDotNetCoreBuild ? [] : [importFrom("System.Threading.Tasks.Extensions").pkg];

@@public
export const fluentAssertionsWorkaround = isDotNetCoreBuild
    ? [
        importFrom("FluentAssertions").pkg,
        importFrom("System.Configuration.ConfigurationManager").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    ] : [
        importFrom("FluentAssertions").pkg,
    ];