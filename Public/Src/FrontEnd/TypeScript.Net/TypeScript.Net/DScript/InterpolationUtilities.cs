// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.DScript
{
    /// <nodoc />
    public static class InterpolationUtilities
    {
        /// <summary>
        /// Returns kind of interpolated string.
        /// </summary>
        public static InterpolationKind GetInterpolationKind(this ITaggedTemplateExpression taggedTemplateExpression)
        {
            Contract.Requires(taggedTemplateExpression != null);

            string tag = taggedTemplateExpression.GetTagText();
            if (string.IsNullOrEmpty(tag))
            {
                return InterpolationKind.StringInterpolation;
            }

            if (tag.Length != 1)
            {
                return InterpolationKind.Unknown;
            }

            switch (tag[0])
            {
                case Names.PathInterpolationFactory:
                    return InterpolationKind.PathInterpolation;
                case Names.FileInterpolationFactory:
                    return InterpolationKind.FileInterpolation;
                case Names.DirectoryInterpolationFactory:
                    return InterpolationKind.DirectoryInterpolation;
                case Names.RelativePathInterpolationFactory:
                    return InterpolationKind.RelativePathInterpolation;
                case Names.PathAtomInterpolationFactory:
                    return InterpolationKind.PathAtomInterpolation;
                default:
                    return InterpolationKind.Unknown;
            }
        }

        /// <summary>
        /// Checks if this tagged template is a path interpolation such that it can contains path separators
        /// </summary>
        public static bool IsPathInterpolation(this ITaggedTemplateExpression taggedTemplateExpression)
        {
            switch (taggedTemplateExpression.GetInterpolationKind())
            {
                case InterpolationKind.FileInterpolation:
                case InterpolationKind.DirectoryInterpolation:
                case InterpolationKind.PathInterpolation:
                case InterpolationKind.RelativePathInterpolation:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns a factory name for a given <paramref name="kind"/>.
        /// </summary>
        public static string GetIdentifierName(this InterpolationKind kind)
        {
            switch (kind)
            {
                case InterpolationKind.StringInterpolation:
                    // This is a legit case
                    return string.Empty;
                case InterpolationKind.PathInterpolation:
                    return new string(Names.PathInterpolationFactory, 1);
                case InterpolationKind.FileInterpolation:
                    return new string(Names.FileInterpolationFactory, 1);
                case InterpolationKind.DirectoryInterpolation:
                    return new string(Names.DirectoryInterpolationFactory, 1);
                case InterpolationKind.PathAtomInterpolation:
                    return new string(Names.PathAtomInterpolationFactory, 1);
                case InterpolationKind.RelativePathInterpolation:
                    return new string(Names.RelativePathInterpolationFactory, 1);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), I($"Unknown interpolation kind '{kind}'."));
            }
        }
    }
}
