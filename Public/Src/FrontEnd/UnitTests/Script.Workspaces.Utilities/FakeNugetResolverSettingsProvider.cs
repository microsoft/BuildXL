// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Workspaces.Utilities
{
    internal sealed class FakeNugetResolverSettingsProvider : IResolverSettingsProvider
    {
        /// <summary>
        /// Do a naive interpretation of an object literal to get NuGet resolver settings
        /// </summary>
        public Possible<IResolverSettings> TryGetResolverSettings(
            IObjectLiteralExpression resolverConfigurationLiteral)
        {
            Contract.Requires(resolverConfigurationLiteral != null);
            var result = new NugetResolverSettings { Kind = KnownResolverKind.NugetResolverKind, Name = "Nuget resolver for a naive configuration provider" };

            return

                // Parse 'configuration' field
                ParseConfigurationField(resolverConfigurationLiteral).Then(nugetConfiguration =>
                {
                    result.Configuration = nugetConfiguration;

                    // Parse 'respositories' field if it exists
                    return ParseRepositoriesField(resolverConfigurationLiteral, result.Repositories).Then(repositories =>

                        // Parse 'packages' field if it exists
                        ParsePackagesField(resolverConfigurationLiteral, result.Packages).Then(packages =>

                            // Parse 'doNotEnforceDependencyVersions' field if it exists
                            ParseDoNotEnforceDependencyVersionsField(resolverConfigurationLiteral).Then(doNotEnforceDependencyVersions =>
                            {
                                if (doNotEnforceDependencyVersions.HasValue)
                                {
                                    result.DoNotEnforceDependencyVersions = doNotEnforceDependencyVersions.Value;
                                }

                                // The NugetResolverSettings have now been completely parsed
                                return new Possible<IResolverSettings>(result);
                            })));
                });
        }

        // Parse 'configuration' field
        private Possible<INugetConfiguration> ParseConfigurationField(IObjectLiteralExpression resolverConfigurationLiteral)
        {
            IExpression expression;

            if (!resolverConfigurationLiteral.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.ConfigurationFieldName, out expression))
            {
                return
                    new MalformedConfigurationFailure(
                        I($"Field '{NaiveConfigurationParsingUtilities.ConfigurationFieldName}' is not present."));
            }

            return ParseNugetConfigurationFrom(expression.As<IObjectLiteralExpression>());
        }

        private Possible<INugetConfiguration> ParseNugetConfigurationFrom(
            [CanBeNull] IObjectLiteralExpression configurationExpression)
        {
            IExpression expression;

            if (configurationExpression == null)
            {
                return new MalformedConfigurationFailure("An object literal with NuGet resolver-specific configuration is expected");
            }

            var result = new NugetConfiguration();

            if (configurationExpression.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.CredentialProvidersFieldName, out expression))
            {
                if (expression.Kind != SyntaxKind.ArrayLiteralExpression)
                {
                    return new MalformedConfigurationFailure("An array literal is expected for NuGet credential providers");
                }

                var credentialProviders = expression.Cast<IArrayLiteralExpression>();
                foreach (var element in credentialProviders.Elements)
                {
                    var elementResult = ParseNugetConfigurationFrom(element.As<IObjectLiteralExpression>());
                    if (!elementResult.Succeeded)
                    {
                        return elementResult;
                    }

                    result.CredentialProviders.Add(elementResult.Result);
                }
            }

            if (configurationExpression.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.ToolUrlFieldName, out expression))
            {
                // TODO: Consider validating that string is well-formed URL
                result.ToolUrl = expression.As<ILiteralExpression>()?.Text;
            }

            if (configurationExpression.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.HashFieldName, out expression))
            {
                result.Hash = expression.As<ILiteralExpression>()?.Text;
            }

            return result;
        }

        // Parse 'respositories' field if it exists
        private static Possible<Dictionary<string, string>> ParseRepositoriesField(IObjectLiteralExpression resolverConfigurationLiteral, Dictionary<string, string> repositories)
        {
            Contract.Assert(repositories != null);

            IExpression expression;

            if (resolverConfigurationLiteral.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.RepositoriesFieldName, out expression))
            {
                var repositoriesExpression = expression.As<IObjectLiteralExpression>();
                if (repositoriesExpression == null)
                {
                    return new MalformedConfigurationFailure("An object literal with NuGet respositories definitions is expected");
                }

                foreach (var property in repositoriesExpression.Properties)
                {
                    if (property.Kind != SyntaxKind.PropertyAssignment)
                    {
                        return new MalformedConfigurationFailure(
                            I($"Field '{NaiveConfigurationParsingUtilities.RepositoriesFieldName}' is supposed to contain property assignments only but property '{property.Name.Text}' is of type {property.Kind}."));
                    }

                    var propertyInitializer = property.Cast<IPropertyAssignment>().Initializer;
                    if (propertyInitializer.Kind != SyntaxKind.StringLiteral)
                    {
                        return new MalformedConfigurationFailure(
                            I($"Field '{NaiveConfigurationParsingUtilities.RepositoriesFieldName}' is supposed to contain string literal property assignments only, but property '{property.Name.Text}' has initializer of type '{propertyInitializer.Kind}'."));
                    }

                    repositories.Add(
                        property.Name.Text,
                        propertyInitializer.Cast<IStringLiteral>().Text);
                }
            }

            return repositories;
        }

        // Parse 'packages' field if it exists
        private static Possible<List<INugetPackage>> ParsePackagesField(IObjectLiteralExpression resolverConfigurationLiteral, List<INugetPackage> packages)
        {
            IExpression expression;

            if (resolverConfigurationLiteral.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.PackagesFieldName, out expression))
            {
                if (expression.Kind != SyntaxKind.ArrayLiteralExpression)
                {
                    return new MalformedConfigurationFailure(
                        I($"An array literal is expected for field '{NaiveConfigurationParsingUtilities.PackagesFieldName}', but it is of type {expression.Kind}"));
                }

                var packagesExpression = expression.Cast<IArrayLiteralExpression>();
                foreach (var packageExpression in packagesExpression.Elements)
                {
                    var maybePackage = ParseNugetPackageFrom(packageExpression.As<IObjectLiteralExpression>());
                    if (!maybePackage.Succeeded)
                    {
                        return maybePackage.Failure;
                    }

                    packages.Add(maybePackage.Result);
                }
            }

            return packages;
        }

        private static Possible<INugetPackage> ParseNugetPackageFrom(
            [CanBeNull] IObjectLiteralExpression packageExpression)
        {
            if (packageExpression == null)
            {
                return new MalformedConfigurationFailure("An object literal with a NuGet package definition is expected");
            }

            string id;
            packageExpression.TryExtractLiteralFromAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.IdFieldName, out id);

            string version;
            packageExpression.TryExtractLiteralFromAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.VersionFieldName, out version);

            string alias;
            packageExpression.TryExtractLiteralFromAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.AliasFieldName, out alias);

            return new NugetPackage
            {
                Id = id,
                Alias = alias,
                Version = version,
            };
        }

        // Parse 'doNotEnforceDependencyVersions' field if it exists
        private static Possible<bool?> ParseDoNotEnforceDependencyVersionsField(IObjectLiteralExpression resolverConfigurationLiteral)
        {
            IExpression expression;

            if (resolverConfigurationLiteral.TryFindAssignmentPropertyInitializer(NaiveConfigurationParsingUtilities.DoNotEnforceDependencyVersionsFieldName, out expression))
            {
                switch (expression.Kind)
                {
                    case SyntaxKind.TrueKeyword:
                        return true;

                    case SyntaxKind.FalseKeyword:
                        return false;

                    default:
                        return new MalformedConfigurationFailure(
                            I($"Field '{NaiveConfigurationParsingUtilities.DoNotEnforceDependencyVersionsFieldName}' is supposed to be a boolean literal, but is of type {expression.Kind}."));
                }
            }

            return (bool?)null;
        }
    }
}
