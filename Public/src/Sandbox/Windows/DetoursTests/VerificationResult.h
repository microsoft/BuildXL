// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

struct VerificationResult {
    bool Succeeded;

    VerificationResult() : Succeeded(true) { }
    VerificationResult(bool value) : Succeeded(value) { }

    void Combine(VerificationResult const& other) {
        Succeeded &= other.Succeeded;
    }
};