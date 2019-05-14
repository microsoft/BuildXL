
/** Returns a list of nuget packages required to use Task/Task<T>/ValueTask<T>. */
@@public
export const tplPackages = isDotNetCoreBuild ? [] : [importFrom("System.Threading.Tasks.Extensions").pkg];

