// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.Sbom.Contracts;
using Microsoft.Sbom.Contracts.Enums;
using Microsoft.VisualStudio.Services.Governance.ComponentDetection;

namespace SBOMConverter
{
    /// <summary>
    /// Generator class for converting <see cref="SBOMPackage"/>.
    /// </summary>
    public class SBOMPackageGenerator
    {
        private static readonly Dictionary<ComponentType, Action<TypedComponent, SBOMPackage>> s_typedComponentMap = new()
        {
            { ComponentType.Cargo, ConvertCargoComponent },
            { ComponentType.Conda, ConvertCondaComponent },
            { ComponentType.DockerImage, ConvertDockerImageComponent },
            { ComponentType.Git, ConvertGitComponent },
            { ComponentType.Go, ConvertGoComponent },
            { ComponentType.Linux, ConvertLinuxComponent },
            { ComponentType.Maven, ConvertMavenComponent },
            { ComponentType.Npm, ConvertNpmComponent },
            { ComponentType.NuGet, ConvertNuGetComponent },
            { ComponentType.Other, ConvertOtherComponent },
            { ComponentType.Pip, ConvertPipComponent },
            { ComponentType.Pod, ConvertPodComponent },
            { ComponentType.RubyGems, ConvertRubyGemsComponent },
        };

        /// <summary>
        /// Tries to convert from <see cref="TypedComponent"/> to <see cref="SBOMPackage"/>.
        /// </summary>
        /// <param name="component">Component to convert.</param>
        /// <param name="logger">Logger to log any issues with conversion.</param>
        /// <param name="package"><see cref="SBOMPackage"/> that was created from the <see cref="TypedComponent"/>.</param>
        /// <returns>True if conversion was successful, and false if not. Warnings will be logged if false is returned, and will return a default <see cref="SBOMPackage"/> object.</returns>
        public static bool TryConvertTypedComponent(TypedComponent component, Action<string> logger, out SBOMPackage package)
        {
            package = default;

            if (component == null)
            {
                ComponentDetectionConverter.Log(logger, "SBOMPackageConverter received a null component.");
                return false;
            }

            if (s_typedComponentMap.TryGetValue(component.Type, out var converter))
            {
                // Add generic attributes
                package = new SBOMPackage()
                {
                    Id = component.Id,
                    PackageUrl = component.PackageUrl?.ToString(),
                };

                // Add component specific attributes
                converter(component, package);

                return true;
            }
            else
            {
                // If the map does not contain this type, then it is most likely a new component type.
                ComponentDetectionConverter.Log(logger, $"SBOMPackageConverter could not find type mapping for TypedComponent of type '{component.Type}'.");
                return false;
            }
        }

        #region Converters
        private static void ConvertCargoComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as CargoComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
        }

        private static void ConvertCondaComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as CondaComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.PackageSource = c.Url;
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    //Algorithm = AlgorithmName.MD5, // Uncomment when MD5 is added in a future release
                    ChecksumValue = c.MD5
                },
            };
        }

        private static void ConvertDockerImageComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as DockerImageComponent;

            package.PackageName = c.Name;
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    Algorithm = AlgorithmName.SHA256,
                    ChecksumValue = c.Digest
                },
            };
        }

        private static void ConvertGitComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as GitComponent;

            package.PackageSource = c.RepositoryUrl.ToString();
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    Algorithm = AlgorithmName.SHA1,
                    ChecksumValue = c.CommitHash
                },
            };
        }

        private static void ConvertGoComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as GoComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    Algorithm = AlgorithmName.SHA256,
                    ChecksumValue = c.Hash
                },
            };
        }

        private static void ConvertLinuxComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as LinuxComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
        }

        private static void ConvertMavenComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as MavenComponent;

            package.PackageVersion = c.Version;
        }

        private static void ConvertNpmComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as NpmComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    ChecksumValue = c.Hash
                },
            };
        }

        private static void ConvertNuGetComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as NuGetComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
        }

        private static void ConvertOtherComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as OtherComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.PackageSource = c.DownloadUrl.ToString();
            package.Checksum = new List<Checksum>()
            {
                new Checksum()
                {
                    ChecksumValue = c.Hash
                },
            };
        }

        private static void ConvertPipComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as PipComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
        }

        private static void ConvertPodComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as PodComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.PackageSource = c.SpecRepo;
        }

        private static void ConvertRubyGemsComponent(TypedComponent component, SBOMPackage package)
        {
            var c = component as RubyGemsComponent;

            package.PackageName = c.Name;
            package.PackageVersion = c.Version;
            package.PackageSource = c.Source;
        }
        #endregion Converters
    }
}