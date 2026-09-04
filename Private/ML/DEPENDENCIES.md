# Pip Usage Training Dependencies

This inventory documents the Python 3.11 dependency closure pinned in `constraints.txt`. Component Governance and CELA guidance remain authoritative for approval and NOTICE/source-disclosure requirements.

These packages run only on the model-training agents. The produced `BuildXL.ML.Models` package contains Microsoft-authored JSON model data and does not redistribute these Python packages.

| Package | Relationship | License |
| --- | --- | --- |
| azure-identity | Direct | MIT |
| azure-kusto-data | Direct | MIT |
| bayesian-optimization | Direct | MIT |
| lightgbm | Direct | MIT |
| numpy | Direct | BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0 |
| pandas | Direct | BSD-3-Clause |
| azure-core | Transitive | MIT |
| certifi | Transitive | MPL-2.0 |
| cffi | Transitive | MIT-0 |
| charset-normalizer | Transitive | MIT |
| colorama | Transitive | BSD-3-Clause |
| cryptography | Transitive | Apache-2.0 OR BSD-3-Clause |
| idna | Transitive | BSD-3-Clause |
| ijson | Transitive | BSD-3-Clause AND ISC |
| joblib | Transitive | BSD-3-Clause |
| msal | Transitive | MIT |
| msal-extensions | Transitive | MIT |
| narwhals | Transitive | MIT |
| packaging | Transitive | Apache-2.0 OR BSD-2-Clause |
| pycparser | Transitive | BSD-3-Clause |
| PyJWT | Transitive | MIT |
| python-dateutil | Transitive | Apache-2.0 OR BSD-3-Clause |
| requests | Transitive | Apache-2.0 |
| scikit-learn | Transitive | BSD-3-Clause |
| scipy | Transitive | BSD-3-Clause with additional permissively licensed bundled components |
| six | Transitive | MIT |
| threadpoolctl | Transitive | BSD-3-Clause |
| typing-extensions | Transitive | PSF-2.0 |
| tzdata | Transitive | Apache-2.0 |
| urllib3 | Transitive | MIT |
| setuptools | Build backend | MIT |

`certifi` provides the CA certificate bundle used by Requests for Azure Kusto TLS connections. It is used only during training and thus is not distributed with BuildXL or `BuildXL.ML.Models`.

`bayesian-optimization` and its dependencies are used only during training and thus are not distributed with BuildXL or `BuildXL.ML.Models`.