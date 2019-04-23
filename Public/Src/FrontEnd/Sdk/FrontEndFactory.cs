// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Wraps all the frontends that can be used during a build
    /// </summary>
    public sealed class FrontEndFactory
    {
        private IConfigurationProcessor m_configurationProcessor;

        private readonly List<IFrontEnd> m_registeredFrontEnds = new List<IFrontEnd>();
        private readonly Dictionary<string, IFrontEnd> m_resolverKinds = new Dictionary<string, IFrontEnd>(StringComparer.Ordinal);

        private readonly Dictionary<EnginePhases, Action> m_phaseStartHooks = new Dictionary<EnginePhases, Action>();
        private readonly Dictionary<EnginePhases, Action> m_phaseEndHooks = new Dictionary<EnginePhases, Action>();

        /// <summary>
        /// Convenience helper for testing a collection of frontends
        /// </summary>
        public static FrontEndFactory CreateInstanceForTesting(Func<IConfigurationProcessor> createConfigurationProcessor, params IFrontEnd[] frontEnds)
        {
            Contract.Requires(createConfigurationProcessor != null);
            Contract.Requires(frontEnds != null);

            var factory = new FrontEndFactory();
            factory.SetConfigurationProcessor(createConfigurationProcessor());
            foreach (var frontEnd in frontEnds)
            {
                Contract.Assert(frontEnd != null);

                factory.AddFrontEnd(frontEnd);
            }

            factory.TrySeal(new LoggingContext("UnitTest"));

            return factory;
        }

        /// <nodoc />
        public void SetConfigurationProcessor(IConfigurationProcessor configurationProcessor)
        {
            Contract.Requires(configurationProcessor != null);
            Contract.Requires(!IsSealed);

            m_configurationProcessor = configurationProcessor;
        }

        /// <summary>
        /// Composes a given hook with any 'start' hook previously associated with a given phase.
        /// </summary>
        public void AddPhaseStartHook(EnginePhases phase, Action hook)
        {
            AddPhaseHook(m_phaseStartHooks, phase, hook);
        }

        /// <summary>
        /// Composes a given hook with any 'end' hook previously associated with a given phase.
        /// </summary>
        public void AddPhaseEndHook(EnginePhases phase, Action hook)
        {
            AddPhaseHook(m_phaseEndHooks, phase, hook);
        }

        /// <nodoc />
        public void AddFrontEnd(IFrontEnd frontEnd)
        {
            m_registeredFrontEnds.Add(frontEnd);
        }

        /// <summary>
        /// Whether this collection is sealed. i.e., no more items can be added.
        /// </summary>
        /// <remarks>
        /// This is mostly exposed for code contracts
        /// </remarks>
        public bool IsSealed { get; private set; }

        /// <summary>
        /// Prevents addition of new frontends and allows querying
        /// </summary>
        public bool TrySeal(LoggingContext loggingContext)
        {
            var registeredFrontEndTypes = new HashSet<Type>();

            foreach (var frontEnd in m_registeredFrontEnds)
            {
                var frontEndType = frontEnd.GetType();

                if (registeredFrontEndTypes.Contains(frontEndType))
                {
                    Logger.Log.DuplicateFrontEndRegistration(loggingContext, frontEnd.GetType().FullName);
                    return false;
                }

                registeredFrontEndTypes.Add(frontEndType);

                foreach (var resolverKind in frontEnd.SupportedResolvers)
                {
                    IFrontEnd existingFrontEnd;
                    if (m_resolverKinds.TryGetValue(resolverKind, out existingFrontEnd))
                    {
                        Logger.Log.DuplicateResolverRegistration(loggingContext, resolverKind, frontEnd.GetType().FullName, existingFrontEnd.GetType().FullName);
                        return false;
                    }

                    m_resolverKinds.Add(resolverKind, frontEnd);
                }
            }

            IsSealed = true;
            return true;
        }

        /// <nodoc />
        public IConfigurationProcessor ConfigurationProcessor
        {
            get
            {
                Contract.Requires(IsSealed);
                return m_configurationProcessor;
            }
        }

        /// <summary>
        /// The list of frontends that are registered with the factory.
        /// </summary>
        /// <remarks>
        /// Note for now these are only new ones. The
        /// </remarks>
        public IEnumerable<IFrontEnd> RegisteredFrontEnds
        {
            get
            {
                Contract.Requires(IsSealed);
                return m_registeredFrontEnds;
            }
        }

        /// <nodoc />
        public bool TryGetFrontEnd(string kind, out IFrontEnd frontEnd)
        {
            Contract.Requires(IsSealed);

            if (m_resolverKinds.TryGetValue(kind, out frontEnd))
            {
                Contract.Assume(frontEnd != null);

                return true;
            }

            return false;
        }

        /// <nodoc />
        public IEnumerable<string> RegisteredFrontEndKinds
        {
            get
            {
                Contract.Requires(IsSealed);

                return m_resolverKinds.Keys;
            }
        }

        /// <summary>
        /// Returns the 'start' hook associated with a given phase.
        /// If no hook has been associated, an empty action is returned.
        /// </summary>
        public Action GetPhaseStartHook(EnginePhases phase)
        {
            return GetPhaseHook(m_phaseStartHooks, phase);
        }

        /// <summary>
        /// Returns the 'end' hook associated with a given phase.
        /// If no hook has been associated, an empty action is returned.
        /// </summary>
        public Action GetPhaseEndHook(EnginePhases phase)
        {
            return GetPhaseHook(m_phaseEndHooks, phase);
        }

        /// <summary>
        /// Composes a given action with any action previously associated with a given key in a given dictionary.
        /// </summary>
        private void AddPhaseHook(Dictionary<EnginePhases, Action> hooks, EnginePhases phase, Action hook)
        {
            Contract.Requires(hooks != null);
            Contract.Requires(hook != null);
            Contract.Requires(!IsSealed);

            var oldHook = GetPhaseHook(hooks, phase);
            hooks[phase] = () =>
            {
                oldHook();
                hook();
            };
        }

        /// <summary>
        /// Returns the action associated with a given key in a given dictionary.  If no entry
        /// is found, returns an empty action.
        /// </summary>
        private static Action GetPhaseHook(Dictionary<EnginePhases, Action> hooks, EnginePhases phase)
        {
            Action hook;
            return hooks.TryGetValue(phase, out hook)
                ? hook
                : () => { };
        }
    }
}
