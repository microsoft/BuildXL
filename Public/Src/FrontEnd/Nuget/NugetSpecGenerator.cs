// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        private readonly PathAtom m_xmlExtension;
        private readonly PathAtom m_pdbExtension;

        /// <nodoc />
        public NugetSpecGenerator(PathTable pathTable, NugetAnalyzedPackage analyzedPackage)
        {
            m_pathTable = pathTable;
            m_analyzedPackage = analyzedPackage;
            m_packageOnDisk = analyzedPackage.PackageOnDisk;

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
        /// const packageRoot = d`absoulte path to the package roo`;
        /// @@public
        /// export const contents: StaticDirectory = Transformer.sealDirectory(
        ///    packageRoot,
        ///    [
        ///       f`${packageRoot}/file`,
        ///    ]);
        /// @@public
        /// export const pkg: NugetPackage = {contents ...};
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public ISourceFile CreateScriptSourceFile(NugetAnalyzedPackage analyzedPackage)
        {
            var sourceFileBuilder = new SourceFileBuilder();

            // 0. Import {Transformer} from "Sdk.Transformers" to be able to seal directories
            sourceFileBuilder.Statement(ImportDeclaration(new [] { "Transformer" }, "Sdk.Transformers"));

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

            // 3. Declare a public directory that points to the package root, for convenience reasons
            sourceFileBuilder
                .Statement(new VariableDeclarationBuilder().Name("packageRoot").Initializer(PropertyAccess("Contents", "packageRoot")).Build())
                .SemicolonAndBlankLine();

            // Create a sealed directory declaration with all the package content
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

            foreach (var framework in analyzedPackage.TargetFrameworkWithFallbacks)
            {
                // Emit the fallback cases first:
                foreach (var fallback in framework.Value)
                {
                    var fallbackString = fallback.ToString(m_pathTable.StringTable);
                    cases.Add(new CaseClause(new LiteralExpression(fallbackString)));
                }

                var compile = new List<IExpression>();
                var runtime = new List<IExpression>();
                var dependencies = new List<IExpression>();

                // Compile items
                if (TryGetValueForFrameworkAndFallbacks(analyzedPackage.References, new NugetTargetFramework(framework.Key), out IReadOnlyList<RelativePath> refAssemblies))
                {
                    foreach (var assembly in refAssemblies)
                    {
                        compile.Add(CreateSimpleBinary(assembly));
                    }
                }

                // Runtime items
                if (TryGetValueForFrameworkAndFallbacks(analyzedPackage.Libraries, new NugetTargetFramework(framework.Key), out IReadOnlyList<RelativePath> libAssemblies))
                {
                    foreach (var assembly in libAssemblies)
                    {
                        runtime.Add(CreateSimpleBinary(assembly));
                    }
                }

                // Dependency items
                if (analyzedPackage.DependenciesPerFramework.TryGetValue(
                    framework.Key,
                    out IReadOnlyList<INugetPackage> dependencySpecificFrameworks))
                {
                    foreach (var dependencySpecificFramework in dependencySpecificFrameworks)
                    {
                        dependencies.Add(CreateImportFromForDependency(dependencySpecificFramework));
                    }
                }

                dependencies.AddRange(analyzedPackage.Dependencies.Select(CreateImportFromForDependency));

                cases.Add(
                    new CaseClause(
                        new LiteralExpression(framework.Key.ToString(m_pathTable.StringTable)),
                        new ReturnStatement(
                            new CallExpression(
                                PropertyAccess("Managed", "Factory", "createNugetPackge"),
                                new LiteralExpression(analyzedPackage.Id),
                                new LiteralExpression(analyzedPackage.Version),
                                PropertyAccess("Contents", "all"),
                                Array(compile),
                                Array(runtime),
                                Array(dependencies)
                            )
                        )
                    )
                );
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
                    (name: "dependencies", Array(package.Dependencies.Select(CreateImportFromForDependency).ToArray())));
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
            var relativepath = "../../../pkgs/" + m_packageOnDisk.Package.Id + "." + m_packageOnDisk.Package.Version;

            return new ModuleDeclaration(
                "Contents",

                Qualifier(new TypeLiteralNode()),
                PathLikeConstVariableDeclaration("packageRoot", InterpolationKind.DirectoryInterpolation, relativepath, Visibility.Export),
                new VariableDeclarationBuilder()
                    .Name("all")
                    .Visibility(Visibility.Public)
                    .Type(new TypeReferenceNode("StaticDirectory"))
                    .Initializer(
                        new CallExpression(
                            new PropertyAccessExpression("Transformer", "sealDirectory"),
                            new Identifier("packageRoot"),
                            new ArrayLiteralExpression(
                                m_packageOnDisk.Contents.OrderBy(path => path.ToString(m_pathTable.StringTable)).Select(GetFileExpressionForPath)
                                    .ToArray())))
                    .Build()
            );
        }

        private bool TryCreateQualifier(NugetAnalyzedPackage analyzedPackage, out IStatement statement)
        {
            if (analyzedPackage.HasTargetFrameworks())
            {
                var targetFrameworks = analyzedPackage.GetTargetFrameworksInStableOrder(m_pathTable);
                // { targetFramework: 'tf1' | 'tf2' }
                var qualifierType = UnionType(propertyName: "targetFramework", literalTypes: targetFrameworks);
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
            // f`{packageRoot}/relativePath`
            return PathLikeLiteral(
                InterpolationKind.FileInterpolation,
                Identifier("packageRoot"),
                "/" + relativePath.ToString(m_pathTable.StringTable, PathFormat.Script));
        }

        private IExpression CreateSimpleBinary(RelativePath binaryFile)
        {
            var pdbPath = binaryFile.ChangeExtension(m_pathTable.StringTable, m_pdbExtension);
            var xmlPath = binaryFile.ChangeExtension(m_pathTable.StringTable, m_xmlExtension);

            return new CallExpression(
                PropertyAccess("Managed", "Factory", "createBinaryFromFiles"),
                GetFileExpressionForPath(binaryFile),
                m_packageOnDisk.Contents.Contains(pdbPath) ? GetFileExpressionForPath(pdbPath) : null,
                m_packageOnDisk.Contents.Contains(xmlPath) ? GetFileExpressionForPath(xmlPath) : null);
        }
    }
}
