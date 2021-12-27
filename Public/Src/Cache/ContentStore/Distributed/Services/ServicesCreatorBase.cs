// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Services
{
    /// <summary>
    /// Base helper class for service creation.
    /// </summary>
    public class ServicesCreatorBase
    {
        /// <summary>
        /// Creates a service which specifies a condition gating its availability
        /// </summary>
        public IServiceDefinition<T> CreateOptional<T>(Func<bool> isAvailable, Func<T> createService)
        {
            return new ServiceDefinition<T>(isAvailable, createService);
        }

        /// <summary>
        /// Creates a service definition
        /// </summary>
        public IServiceDefinition<T> Create<T>(Func<T> createService)
        {
            return new ServiceDefinition<T>(() => true, createService);
        }
    }

    /// <summary>
    /// Defines a service instantiation
    /// </summary>
    public interface IServiceDefinition<out T>
    {
        /// <summary>
        /// Checks if the service is available
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Gets the singleton instance of the service
        /// </summary>
        T Instance { get; }
    }

    /// <summary>
    /// Defines a service instantiation
    /// </summary>
    public class ServiceDefinition<T> : IServiceDefinition<T>
    {
        /// <inheritdoc />
        public bool IsAvailable => _isAvailable.Value;

        /// <inheritdoc />
        public T Instance => IsAvailable ? _instance.Value : throw new Exception("Service not available");

        private readonly Lazy<T> _instance;
        private readonly Lazy<bool> _isAvailable;

        /// <nodoc />
        public ServiceDefinition(Func<bool> isAvailable, Func<T> createService)
        {
            _instance = new(createService);
            _isAvailable = new(isAvailable);
        }
    }

    /// <summary>
    /// Wraps service so that checking availability of service is required to get instance
    /// </summary>
    public struct OptionalServiceDefinition<T>
    {
        private readonly IServiceDefinition<T>? _serviceDefinition;

        public OptionalServiceDefinition(IServiceDefinition<T> serviceDefinition)
        {
            _serviceDefinition = serviceDefinition;
        }

        /// <summary>
        /// Attempts to get instance of service if it is available
        /// </summary>
        public bool TryGetInstance([NotNullWhen(true)] out T? instance)
        {
            if (_serviceDefinition?.IsAvailable == true)
            {
                instance = _serviceDefinition.Instance!;
                return true;
            }
            else
            {
                instance = default;
                return false;
            }
        }

        /// <summary>
        /// Attempts to get instance of service if it is available
        /// </summary>
        public T? InstanceOrDefault()
        {
            return TryGetInstance(out var instance) ? instance : default;
        }
    }

    /// <nodoc />
    public static class ServiceDefinitionExtensions
    {
        /// <summary>
        /// Wraps service so that checking availability of service is required to get instance
        /// </summary>
        public static OptionalServiceDefinition<T> AsOptional<T>(this IServiceDefinition<T> serviceDefinition)
        {
            return new OptionalServiceDefinition<T>(serviceDefinition);
        }
    }
}