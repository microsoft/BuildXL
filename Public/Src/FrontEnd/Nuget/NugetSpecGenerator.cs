// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using static TypeScript.Net.DScript.SyntaxFactory;
using static BuildXL.FrontEnd.Nuget.SyntaxFactoryEx;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Script.Literals;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    ///     Helper to generate domionscript specs for nuget packages
    /// </summary>
    public sealed class NugetSpecGenerator
    {
        private readonly PathTable m_pathTable;
        private readonly PackageOnDisk m_packageOnDisk;
        private readonly NugetAnalyzedPackage m_analyzedPackage;
        private readonly IReadOnlyDictionary<string, string> m_repositories;
        private readonly NugetFrameworkMonikers m_nugetFrameworkMonikers;
        private readonly AbsolutePath m_sourceDirectory;
        private readonly PathAtom m_xmlExtension;
        private readonly PathAtom m_pdbExtension;
        private readonly int? m_timeoutInMinutes;

        /// <summary>Current spec generation format version</summary>
        public const int SpecGenerationFormatVersion = 12;

        /// <nodoc />
        public NugetSpecGenerator(
            PathTable pathTable, 
            NugetAnalyzedPackage analyzedPackage, 
            IReadOnlyDictionary<string, string> repositories,
            AbsolutePath sourceDirectory,
            int? timeoutInMinutes = null)
        {
            m_pathTable = pathTable;
            m_analyzedPackage = analyzedPackage;
            m_repositories = repositories;
            m_packageOnDisk = analyzedPackage.PackageOnDisk;
            m_nugetFrameworkMonikers = new NugetFrameworkMonikers(pathTable.StringTable);
            m_sourceDirectory = sourceDirectory;
            m_timeoutInMinutes = timeoutInMinutes;

            m_xmlExtension = PathAtom.Create(pathTable.StringTable, ".xml");
            m_pdbExtension = PathAtom.Create(pathTable.StringTable, ".pdb");
        }

        /// <summary>
        /// Generates a DScript spec for a given <paramref name="analyzedPackage"/>.
        /// </summary>
        /// <remarks>
        /// The generated format is:
        /// [optional] import of managed sdk core
        /// [optional] qualifier declaration
        /// @@public
        /// export const contents: StaticDirectory = NuGetDownloader.downloadPackage(
        ///    {
        ///     id: "package ID",
        ///     version: "X.XX",
        ///     ...
        ///    }
        /// @@public
        /// export const pkg: NugetPackage = {contents ...};
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public ISourceFile CreateScriptSourceFile(NugetAnalyzedPackage analyzedPackage)
        {
            var sourceFileBuilder = new SourceFileBuilder();

            // 0. Import * as NugetDownloader from "BuildXL.Tools.NugetDownloader" to be able to download NuGet packages
            sourceFileBuilder.Statement(ImportDeclaration("NugetDownloader", "BuildXL.Tools.NugetDownloader"));

            // 1. Optional import of managed sdk.
            if (analyzedPackage.IsManagedPackage)
            {
                // import * as Managed from 'Sdk.Managed';
                sourceFileBuilder
                    .Statement(ImportDeclaration(alias: "Managed", moduleName: "Sdk.Managed"))
                    .SemicolonAndBlankLine();
            }

            // 2. Optional qualifier
            if (TryCreateQualifier(analyzedPackage, out var qualifierStatement))
            {
                sourceFileBuilder
                    .Statement(qualifierStatement)
                    .SemicolonAndBlankLine();
            }

            // Create a seal directory declaration with all the package content
            sourceFileBuilder
                .Statement(CreatePackageContents())
                .SemicolonAndBlankLine();

            // @@public export const pkg = ...
            sourceFileBuilder.Statement(CreatePackageVariableDeclaration(analyzedPackage));

            return sourceFileBuilder.Build();
        }

        /// <nodoc />
        public ISourceFile CreatePackageConfig()
        {
            var packageId = string.IsNullOrEmpty(m_packageOnDisk.Package.Alias)
                ? m_packageOnDisk.Package.Id
                : m_packageOnDisk.Package.Alias;

            return new ModuleConfigurationBuilder()
                .Name(packageId)
                .Version(m_packageOnDisk.Package.Version)
                // The generated module is V2 module.
                .NameResolution(implicitNameResolution: true)
                .Build();
        }

        private List<ICaseClause> CreateSwitchCasesForTargetFrameworks(NugetAnalyzedPackage analyzedPackage, ITypeNode pkgType)
        {
            var cases = new List<ICaseClause>();
            Contract.Assert(analyzedPackage.TargetFrameworks.Count != 0, "Managed package must have at least one target framework.");

            var valid = analyzedPackage.TargetFrameworks.Exists(moniker => m_nugetFrameworkMonikers.FullFrameworkVersionHistory.Contains(moniker) || m_nugetFrameworkMonikers.NetCoreVersionHistory.Contains(moniker));
            Contract.Assert(valid, "Target framework monikers must exsist and be registered with internal target framework version helpers.");

            var allFullFrameworkDeps = m_nugetFrameworkMonikers.FullFrameworkVersionHistory
                .SelectMany(m =>
                    analyzedPackage.DependenciesPerFramework.TryGetValue(m, out IReadOnlyList<INugetPackage> dependencySpecificFrameworks)
                        ? dependencySpecificFrameworks
                        : new List<INugetPackage>())
                .GroupBy(pkg => pkg.Id)
                .Select(grp => grp.OrderBy(pkg => pkg.Version).Last());

            foreach (var versionHistory in new List<PathAtom>[] { m_nugetFrameworkMonikers.FullFrameworkVersionHistory, m_nugetFrameworkMonikers.NetCoreVersionHistory })
            {
                FindAllCompatibleFrameworkMonikers(analyzedPackage, (List<PathAtom> monikers) =>
                {
                    if (monikers.Count == 0)
                    {
                        return;
                    }

                    if (analyzedPackage.NeedsCompatibleFullFrameworkSupport &&
                        // Let's add the full framework compatible cases if we are in the netstandard case only
                        !m_nugetFrameworkMonikers.NetCoreAppVersionHistory.Contains(monikers.First())
                      )
                    {
                        cases.AddRange(m_nugetFrameworkMonikers.NetStandardToFullFrameworkCompatibility.Select(m => new CaseClause(new LiteralExpression(m.ToString(m_pathTable.StringTable)))));
                    }

                    cases.AddRange(monikers.Take(monikers.Count - 1).Select(m => new CaseClause(new LiteralExpression(m.ToString(m_pathTable.StringTable)))));

                    var compile = new List<IExpression>();
                    var runtime = new List<IExpression>();
                    var dependencies = new List<IExpression>();

                    // Compile items
                    if (TryGetValueForFrameworkAndFallbacks(analyzedPackage.References, new NugetTargetFramework(monikers.First()), out IReadOnlyList<RelativePath> refAssemblies))
                    {
                        compile.AddRange(refAssemblies.Select(r => CreateSimpleBinary(r)));
                    }

                    // Runtime items
                    if (TryGetValueForFrameworkAndFallbacks(analyzedPackage.Libraries, new NugetTargetFramework(monikers.First()), out IReadOnlyList<RelativePath> libAssemblies))
                    {
                        runtime.AddRange(libAssemblies.Select(l => CreateSimpleBinary(l)));
                    }

                    // For full framework dependencies we unconditionally include all the distinct dependencies from the nuspec file,
                    // .NETStandard dependencies are only included if the moniker and the parsed target framework match!
                    if (m_nugetFrameworkMonikers.IsFullFrameworkMoniker(monikers.First()))
                    {
                        dependencies.AddRange(allFullFrameworkDeps.Select(dep => CreateImportFromForDependency(dep)));
                    }
                    else
                    {
                        if (analyzedPackage.DependenciesPerFramework.TryGetValue(monikers.First(), out IReadOnlyList<INugetPackage> dependencySpecificFrameworks))
                        {
                            dependencies.AddRange(dependencySpecificFrameworks.Select(dep => CreateImportFromForDependency(dep)));
                        }
                    }

                    cases.Add(
                        new CaseClause(
                            new LiteralExpression(monikers.Last().ToString(m_pathTable.StringTable)),
                            new ReturnStatement(
                                Call(
                                    PropertyAccess("Managed", "Factory", "createNugetPackage"),
                                    new LiteralExpression(analyzedPackage.Id),
                                    new LiteralExpression(analyzedPackage.Version),
                                    PropertyAccess("Contents", "all"),
                                    Array(compile),
                                    Array(runtime),
                                    m_nugetFrameworkMonikers.IsFullFrameworkMoniker(monikers.Last())
                                        ? Array(dependencies)
                                        : Array(new CallExpression(new Identifier("...addIfLazy"),
                                            new BinaryExpression(
                                                new PropertyAccessExpression("qualifier", "targetFramework"),
                                                SyntaxKind.EqualsEqualsEqualsToken,
                                                new LiteralExpression(monikers.First().ToString(m_pathTable.StringTable))
                                            ),
                                            new ArrowFunction(
                                                CollectionUtilities.EmptyArray<IParameterDeclaration>(),
                                                Array(dependencies)
                                            )
                                        ))
                                )
                            )
                        )
                    );
                }, versionHistory);
            }

            return cases;
        }

        private bool TryGetValueForFrameworkAndFallbacks<TValue>(
            IReadOnlyDictionary<NugetTargetFramework, TValue> map,
            NugetTargetFramework framework,
            out TValue value)
        {
            if (map.TryGetValue(framework, out value))
            {
                return true;
            }

            return false;
        }


        private IStatement CreatePackageVariableDeclaration(NugetAnalyzedPackage package)
        {
            IExpression pkgExpression;
            TypeReferenceNode pkgType;
            if (package.IsManagedPackage)
            {
                // If the package is managed, it is a 'ManagedNugetPackage' and we create a switch based on the current qualifie
                // that defines contents, compile, runtime and dependency items
                pkgType = new TypeReferenceNode("Managed", "ManagedNugetPackage");

                // Computes the switch cases, based on the target framework
                List<ICaseClause> cases = CreateSwitchCasesForTargetFrameworks(package, pkgType);

                pkgExpression = new CallExpression(
                    new ParenthesizedExpression(
                        new ArrowFunction(
                            CollectionUtilities.EmptyArray<IParameterDeclaration>(),
                            new SwitchStatement(
                                PropertyAccess("qualifier", "targetFramework"),
                                new DefaultClause(
                                    new ExpressionStatement(
                                        new CallExpression(
                                            PropertyAccess("Contract", "fail"),
                                            new LiteralExpression("Unsupported target framework")))),
                                cases))));
            }
            else
            {
                // If the package is not managed, it is a 'NugetPackage' with just contents and dependencies
                pkgType = new TypeReferenceNode("NugetPackage");
                pkgExpression = ObjectLiteral(
                    (name: "contents", PropertyAccess("Contents", "all")),
                    (name: "dependencies", Array(package.Dependencies.Select(CreateImportFromForDependency).ToArray())),
                    (name: "version", new LiteralExpression(package.Version)));
            }

            return
                new VariableDeclarationBuilder()
                    .Name("pkg")
                    .Visibility(Visibility.Public)
                    .Initializer(pkgExpression)
                    .Type(pkgType)
                    .Build();
        }

        private IStatement CreatePackageContents()
        {
            // Arguments for calling the nuget downloader SDK
            var downloadCallArgs = new List<(string, IExpression expression)>(4) 
            {
                    ("id", new LiteralExpression(m_analyzedPackage.Id)),
                    ("version", new LiteralExpression(m_analyzedPackage.Version)),
                    ("downloadDirectory", Identifier("outputDir")),
                    ("extractedFiles", new ArrayLiteralExpression(m_analyzedPackage.PackageOnDisk.Contents
                        .Select(relativePath => PathLikeLiteral(InterpolationKind.RelativePathInterpolation, relativePath.ToString(m_pathTable.StringTable, PathFormat.Script))))),
                    ("repositories", new ArrayLiteralExpression(m_repositories.Select(kvp => new ArrayLiteralExpression(new LiteralExpression(kvp.Key), new LiteralExpression(kvp.Value)))))
            };

            // If a credential provider was used to inspect the package, pass it as an argument to be able to retrieve it.
            if (m_analyzedPackage.CredentialProviderPath.IsValid)
            {
                // If the credential provider is within the source tree, express it in terms of a mount, so the generated
                // spec is more resilient to cache hits across machines
                IExpression path;
                if (m_sourceDirectory.TryGetRelative(m_pathTable, m_analyzedPackage.CredentialProviderPath, out var relativeCredentialProviderPath))
                {
                    path = PathLikeLiteral(
                        InterpolationKind.FileInterpolation, 
                        new PropertyAccessExpression(new CallExpression(new PropertyAccessExpression("Context", "getMount"), new LiteralExpression("SourceRoot")), "path") , 
                        "/" + relativeCredentialProviderPath.ToString(m_pathTable.StringTable, PathFormat.Script));
                }
                else
                {
                    path = PathLikeLiteral(InterpolationKind.FileInterpolation, m_analyzedPackage.CredentialProviderPath.ToString(m_pathTable, PathFormat.Script));
                }
                
                downloadCallArgs.Add(("credentialProviderPath", path));
            }

            if (m_timeoutInMinutes != null)
            {
                downloadCallArgs.Add(("timeoutInMinutes", new LiteralExpression(m_timeoutInMinutes.Value)));
            }

            return new ModuleDeclaration(
                "Contents",

                Qualifier(new TypeLiteralNode()),

                new VariableDeclarationBuilder().Name("outputDir").Visibility(Visibility.None).Type(new TypeReferenceNode("Directory")).Initializer(
                    new CallExpression(new PropertyAccessExpression("Context", "getNewOutputDirectory"), new LiteralExpression("nuget"))).Build(),
                
                new VariableDeclarationBuilder()
                    .Name("all")
                    .Visibility(Visibility.Public)
                    .Type(new TypeReferenceNode("StaticDirectory"))
                    .Initializer(
                                new CallExpression(
                                        new PropertyAccessExpression("NugetDownloader", "downloadPackage"),
                                        ObjectLiteral(downloadCallArgs.ToArray())))
                    .Build()
            );
        }

        internal static void FindAllCompatibleFrameworkMonikers(NugetAnalyzedPackage analyzedPackage, Action<List<PathAtom>> callback, params List<PathAtom>[] tfmHistory)
        {
            if (analyzedPackage.TargetFrameworks.Count > 0)
            {
                foreach (var versionHistory in tfmHistory)
                {
                    var indices = analyzedPackage.TargetFrameworks
                        .Select(moniker => versionHistory.IndexOf(moniker))
                        .Where(idx => idx != -1)
                        .OrderBy(x => x).ToList();

                    if (indices.IsNullOrEmpty())
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count(); i++)
                    {
                        int start = indices[i];
                        int count = (i + 1) > indices.Count() - 1 ? versionHistory.Count() - start : (indices[i + 1] - indices[i]);

                        callback(versionHistory.GetRange(start, count));
                    }
                }
            }
            else
            {
                callback(default(List<PathAtom>));
            }
        }

        private bool TryCreateQualifier(NugetAnalyzedPackage analyzedPackage, out IStatement statement)
        {
            List<PathAtom> compatibleTfms = new List<PathAtom>();

            if (analyzedPackage.NeedsCompatibleFullFrameworkSupport)
            {
                compatibleTfms.AddRange(m_nugetFrameworkMonikers.NetStandardToFullFrameworkCompatibility);
            }

            FindAllCompatibleFrameworkMonikers(analyzedPackage,
                (List<PathAtom> monikers) => compatibleTfms.AddRange(monikers),
                m_nugetFrameworkMonikers.FullFrameworkVersionHistory,
                m_nugetFrameworkMonikers.NetCoreVersionHistory);

            if (compatibleTfms.Count > 0)
            {
                // { targetFramework: 'tf1' | 'tf2' | ... }
                var qualifierType = UnionType(
                    (propertyName: "targetFramework", literalTypes: compatibleTfms.Select(m => m.ToString(m_pathTable.StringTable))),
                    (propertyName: "targetRuntime", literalTypes: m_nugetFrameworkMonikers.SupportedTargetRuntimes)
                );
                statement = Qualifier(qualifierType);
                return true;
            }

            statement = null;
            return false;
        }

        private static IPropertyAccessExpression CreateImportFromForDependency(INugetPackage dependency)
        {
            // TODO: This is a terrible hack but we have same-named incompatible modules from the cache, this was the only workaround to get it working
            string importName = string.IsNullOrEmpty(dependency.Alias) || (dependency.Id == "BuildXL.Cache.ContentStore.Interfaces")
                ? dependency.Id
                : dependency.Alias;

            // importFrom('moduleName').pkg
            return PropertyAccess(
                // TODO: Support multiple SxS versions, so this dependency would be the direct dependency.
                ImportFrom(importName),
                "pkg");
        }

        private IExpression GetFileExpressionForPath(RelativePath relativePath)
        {
            // all.assertExistence(r`relativePath`)
            return new CallExpression(new PropertyAccessExpression("Contents", "all", "getFile"), PathLikeLiteral(
                InterpolationKind.RelativePathInterpolation,
                relativePath.ToString(m_pathTable.StringTable, PathFormat.Script)));
        }

        private IExpression CreateSimpleBinary(RelativePath binaryFile)
        {
            var pdbPath = binaryFile.ChangeExtension(m_pathTable.StringTable, m_pdbExtension);
            var xmlPath = binaryFile.ChangeExtension(m_pathTable.StringTable, m_xmlExtension);

            return Call(
                PropertyAccess("Managed", "Factory", "createBinaryFromFiles"),
                GetFileExpressionForPath(binaryFile),
                m_packageOnDisk.Contents.Contains(pdbPath) ? GetFileExpressionForPath(pdbPath) : null,
                m_packageOnDisk.Contents.Contains(xmlPath) ? GetFileExpressionForPath(xmlPath) : null);
        }
    }
}
