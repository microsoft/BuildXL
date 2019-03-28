// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Engine.Visualization
{
    /// <summary>
    /// A container that knows about the various states
    /// </summary>
    /// <typeparam name="T">The type of value in the container</typeparam>
    public sealed class ValueContainer<T>
        where T : class
    {
        private T m_value;

        /// <summary>
        /// The state of the container
        /// </summary>
        public VisualizationValueState State { get; private set; }

        /// <summary>
        /// Returns the value when state is Available. Else it will throw a ValueNotYetAvailableException
        /// </summary>
        public T Value
        {
            get
            {
                if (State != VisualizationValueState.Available)
                {
                    throw new ValueNotAvailableException(State);
                }

                return m_value;
            }
        }

        /// <summary>
        /// The value of the container. Only non-null when state is Available
        /// </summary>
        public T TryGetValue()
        {
            return m_value;
        }

        /// <summary>
        /// Constructs a new value
        /// </summary>
        public ValueContainer(VisualizationValueState state)
        {
            Contract.Requires(state == VisualizationValueState.Disabled || state == VisualizationValueState.NotYetAvailable);
            State = state;
        }

        /// <summary>
        /// Constructs a new ValueContainer with a value that exists
        /// </summary>
        public void MakeAvailable(T value)
        {
            Contract.Requires(State == VisualizationValueState.NotYetAvailable);

            m_value = value;
            State = VisualizationValueState.Available;
        }

        /// <summary>
        /// If one has to retract the information i.e., in case of failure
        /// </summary>
        public void MakeUnavailable()
        {
            Contract.Requires(State == VisualizationValueState.NotYetAvailable);

            State = VisualizationValueState.Disabled;
        }
    }
}
