// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Base interface for resolver settings. These settings correspond to sections
    /// in the enlistment's root config.dsc, in the resolvers:[] array, tagged with
    /// 'kind' properties to cause the config resolver to transfer settings to
    /// the members of implementations of this interface. See
    /// BuildXL.Utilities.Configuration.Mutable.ResolverSettings
    /// for the common base class for specific settings implementations, and derived
    /// classes for specific implementations.
    /// </summary>
    public interface IResolverSettings
    {
        /// <summary>
        /// The type of resolver.
        /// </summary>
        string Kind { get; }

        /// <summary>
        /// Name of the resolver (useful for debugging and error reporting).
        /// </summary>
        /// <remarks>
        /// Currently, we allow the name to be null, i.e., anonymous.
        /// </remarks>
        string Name { get; }

        /// <summary>
        /// Position in a file where this was configured (if available).
        /// </summary>
        LineInfo Location { get; }

        /// <summary>
        /// File in which this was configured (if available).
        /// </summary>
        AbsolutePath File { get; }

        /// <summary>
        /// Whether the source directory should be allowed to be writable
        /// </summary>
        bool AllowWritableSourceDirectory { get; }

        /// <summary>
        /// Resolvers can opt-in to request full reparse point resolving
        /// </summary>
        /// <remarks>
        /// Full reparse point resolving is not on by default. Resolvers can request the feature by
        /// turning on this flag, even though the final configuration may not have the feature enabled if the user
        /// explicitly disabled it.
        /// </remarks>
        bool RequestFullReparsePointResolving { get; }

        /// <summary>
        /// Allows the resolver name be set (once) in case the user did not configure it in the configuration file.
        /// </summary>
        void SetName(string name);
    }

    /// <summary>
    /// Extension methods for <see cref="IResolverSettings"/>.
    /// </summary>
    public static class ResolverSettingsExtensions
    {
        /// <summary>
        /// If both <see cref="IResolverSettings.File"/> and <see cref="IResolverSettings.Location"/> information
        /// are present, it returns in the format of $"{file}:({line}, {position}): "; else, returns empty string.
        /// </summary>
        public static string GetLocationInfo(this IResolverSettings resolver, PathTable pathTable)
        {
            return resolver.File.IsValid && resolver.Location.IsValid
                ? I($"{resolver.File.ToString(pathTable)}:({resolver.Location.Line}, {resolver.Location.Position}): ")
                : string.Empty;
        }

        /// <summary>
        /// Returns the resolver setting <see cref="BuildXL.Utilities.Instrumentation.Common.Location"/>
        /// </summary>
        public static Location Location(this IResolverSettings resolverSettings, PathTable pathTable)
        {
            return new Location { File = resolverSettings.File.ToString(pathTable), Line = resolverSettings.Location.Line, Position = resolverSettings.Location.Position };
        }
    }
}
