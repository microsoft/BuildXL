# Microsoft.Build.Prediction
NOTE: Forked from an MS-internal repo 'BuildPrediction' as part of public release of this codebase. This work may be moved into a separate GitHub repo. Text below was adapted for this context.

This direcotory generates the Microsoft.Build.Prediction assembly, a library containing predictors that run against evaluated MSBuild [Project]([https://docs.microsoft.com/en-us/dotnet/api/microsoft.build.evaluation.project?view=netframework-4.7.2]) instances to predict file and directory inputs that will be read, and output directories that will be written, by the project.

Predictors are implementations of the IProjectStaticPredictor interface. Execution logic in this library applies the predictors in parallel to a given Project. The library aggregates results from all predictors into a final set of predicted inputs and outputs for a Project.

Input and output predictions produced here can be used, for instance, for Project build caching and sandboxing. Predicted inputs are added to the project file itself and known global files and folders from SDKs and tools to produce a set of files and folders that can be hashed and summarized to produce an inputs hash that can be used to look up the results of previous build executions. The more accurate and complete the predicted set of inputs, the narrower the set of cached output results, and the better the cache performance. Predicted build output directories are used to guide static construction and analysis of build sandboxes.

## Usage

### Custom Plug-in Assemblies

## Design
See [Design](Design.md).

## License
Microsoft.Build.Prediction is licensed under the [MIT License](https://github.com/Microsoft/msbuild/blob/master/LICENSE).
