// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Secrets
{
    /// <nodoc />
    public enum SecretKind
    {
        /// <nodoc />
        PlainText,

        /// <nodoc />
        SasToken
    }

    /// <nodoc />
    public abstract class Secret : IEquatable<Secret>
    {
        /// <inheritdoc />
        public abstract bool Equals(Secret? other);

        /// <nodoc />
        public abstract override int GetHashCode();

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Secret)obj);
        }

    }

    /// <nodoc />
    public class PlainTextSecret : Secret
    {
        /// <nodoc />
        public string Secret { get; }

        /// <nodoc />
        public PlainTextSecret(string secret)
        {
            Contract.Requires(!string.IsNullOrEmpty(secret));
            Secret = secret;
        }

        /// <inheritdoc />
        public override bool Equals(Secret? other)
        {
            if (other is null)
            {
                return false;
            }

            return Secret == ((PlainTextSecret)other).Secret;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Secret.GetHashCode();
        }
    }

    /// <nodoc />
    public sealed class SasToken : IEquatable<SasToken>
    {
        /// <nodoc />
        public string Token { get; }

        /// <nodoc />
        public string StorageAccount { get; }

        /// <nodoc />
        public string? ResourcePath { get; init; }

        /// <nodoc />
        public SasToken(string token, string storageAccount, string? resourcePath = null)
        {
            Token = token;
            StorageAccount = storageAccount;
            ResourcePath = resourcePath;
        }

        /// <inheritdoc />
        public bool Equals(SasToken? other)
        {
            if (other is null)
            {
                return false;
            }

            return Token == other.Token && StorageAccount == other.StorageAccount && ResourcePath == other.ResourcePath;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as SasToken);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Token, StorageAccount, ResourcePath ?? string.Empty).GetHashCode();
        }
    }

    /// <nodoc />
    public class UpdatingSasToken : Secret
    {
        /// <nodoc />
        public SasToken Token { get; private set; }

        /// <nodoc />
        public event EventHandler<SasToken>? TokenUpdated;

        /// <nodoc />
        public UpdatingSasToken(SasToken token)
        {
            Token = token;
        }

        /// <nodoc />
        public void UpdateToken(SasToken token)
        {
            Contract.Requires(token != null);

            Token = token;
            TokenUpdated?.Invoke(this, token);
        }

        /// <inheritdoc />
        public override bool Equals(Secret? other)
        {
            if (other is not UpdatingSasToken otherToken)
            {
                return false;
            }

            return Token.Equals(otherToken.Token);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Token.GetHashCode();
        }
    }
}
