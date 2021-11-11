// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.LogGen.Core;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;

namespace BuildXL.LogGen.Generators
{
    internal sealed class SupportedGenerators
    {
        /// <summary>
        /// Mapping of Generator types to a factory to create a new generator.
        /// </summary>
        public static Dictionary<EventGenerators, Func<GeneratorBase>> Generators = new Dictionary<EventGenerators, Func<GeneratorBase>>()
        {
            { EventGenerators.InspectableLogger, () => new InspectableEventSourceGenerator() },
            { EventGenerators.ManifestedEventSource, () => new ManifestedEventSource() },
#if FEATURE_ARIA_TELEMETRY
            { EventGenerators.AriaV2, () => new AriaV2() },
#else
            { EventGenerators.AriaV2, () => new Noop() },
#endif
            { EventGenerators.Statistics, () => new BuildXLStatistic() },
        };
    }
}