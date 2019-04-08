// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

struct VerificationResult {
    bool Succeeded;

    VerificationResult() : Succeeded(true) { }
    VerificationResult(bool value) : Succeeded(value) { }

    void Combine(VerificationResult const& other) {
        Succeeded &= other.Succeeded;
    }
};
