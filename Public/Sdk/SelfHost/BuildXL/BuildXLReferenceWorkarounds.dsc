
/** Returns a list of nuget packages required to use Task/Task<T>/ValueTask<T>. */
@@public
export const tplPackages = isDotNetCoreBuild ? [] : [importFrom("System.Threading.Tasks.Extensions").pkg];


/**
 * Our nuget ingegration generates the wrong code for netcoreapp2.2 for System.Configuration.ConfigurationManager 
 * It should have added the netstandard2.0 version of the library for netcoreapp2.2 but because the config has
 * a seperate group for netcoreapp2.2 our current logic mistakenly thinks there are no references to add for netcoreapp2.2
 * We therefore temporary expose this assembly manually until we fix the nuget generation
 **/

 @@public
export const fluentAssertionsWorkaround = isDotNetCoreBuild 
    ? [
        importFrom("FluentAssertions").pkg,
        importFrom("System.Configuration.ConfigurationManager").withQualifier({targetFramework: "netstandard2.0"}).pkg,
    ] : [
        importFrom("FluentAssertions").pkg,
    ];
