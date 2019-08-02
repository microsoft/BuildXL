// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed
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
    public abstract class Secret
    {
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
    }

    /// <nodoc />
    public class SasToken
    {
        /// <nodoc />
        public string Token { get; set; }

        /// <nodoc />
        public string StorageAccount { get; set; }

        /// <nodoc />
        public string ResourcePath { get; set; }
    }

    /// <nodoc />
    public class UpdatingSasToken : Secret
    {
        /// <nodoc />
        public SasToken Token { get; private set; }

        /// <nodoc />
        public event EventHandler<SasToken> TokenUpdated;

        /// <nodoc />
        public UpdatingSasToken(SasToken token)
        {
            UpdateToken(token);
        }

        /// <nodoc />
        public void UpdateToken(SasToken token)
        {
            Contract.Requires(token != null);
            Token = token;
            TokenUpdated?.Invoke(this, token);
        }
    }
}
