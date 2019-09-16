using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.Core.ML.Classifier
{
    /// <summary>
    ///  The content placement classifier, which given an artifact and a queue can choose a set of machines
    ///  as targets for its replication
    /// </summary>
    public class ContentPlacementClassifier : IMLClassifier<ContentPlacementInstance, ContentPlacementClassifierResult>
    {
        private static readonly string s_sharedClassLabel = MLArtifact.SharingClassLabels[0];
        
        /// <nodoc />
        public enum LoadMode
        {
            /// <nodoc />
            MachinesFromClosestQueue,
            /// <nodoc />
            NMachinesPerClosestQueues
        }

        private readonly string m_configPath;

        /// <summary>
        ///  The random forest to decide on artifact inputs
        /// </summary>
        private RandomForest m_forest = null;
        /// <summary>
        ///  The return values (machines) for each queue, preloaded
        /// </summary>
        private readonly Dictionary<string, List<string>> m_alternativesPerQueue = new Dictionary<string, List<string>>();

        /// <summary>
        ///  The argument is the path to a valid configutation file which follows the template of ContentPlacementClassifierConfiguration
        /// </summary>
        public ContentPlacementClassifier(string config)
        {
            Contract.Requires(config != null, "Config file path cannot be null");
            m_configPath = config;
        }

        /// <summary>
        ///  The argument is the path to a valid ContentPlacementClassifierConfiguration
        /// </summary>
        public ContentPlacementClassifier(ContentPlacementClassifierConfiguration config)
        {
            Contract.Requires(config != null, "Config file path cannot be null");
            LoadClassifier(config);
        }

        /// <nodoc />
        public Task<Result<bool>> StartupAsync()
        {
            try
            {
                var configuration = ContentPlacementClassifierConfiguration.FromJson(m_configPath);
                LoadClassifier(configuration);
                return Task.FromResult(new Result<bool>(true));
            }
            catch (Exception e)
            {
                return Task.FromResult(new Result<bool>(e));
            }
        }

        /// <summary>
        ///  Return the set of alternatives (machines) per queue
        /// </summary>
        public Dictionary<string, List<string>> AlternativesPerQueue()
        {
            return m_alternativesPerQueue;
        }

        private void LoadClassifier(ContentPlacementClassifierConfiguration config)
        {
            Contract.Requires(Directory.Exists(config.QueueToMachineMapDirectory), $"QueToMachineMapDirectory directory [{config.QueueToMachineMapDirectory}] does not exists");
            Contract.Requires(Directory.Exists(config.QueueDistanceMapDirectory), $"QueueDistanceMapDirectory directory [{config.QueueDistanceMapDirectory}] does not exists");
            Contract.Requires(File.Exists(config.RandomForestFile), $"RandomForestFile [{config.RandomForestFile}] does not exists");
            // load the forest
            m_forest = RandomForest.FromWekaFile(config.RandomForestFile);
            // now load the alternatives per queue
            LoadAlternativesPerQueue(config);
            // done
        }

        private void LoadAlternativesPerQueue(ContentPlacementClassifierConfiguration config)
        {
            // for each queue
            foreach (var queue in Directory.EnumerateFiles(config.QueueDistanceMapDirectory))
            {
                var queueName = Path.GetFileNameWithoutExtension(queue);
                var queueFileReader = new StreamReader(queue);
                m_alternativesPerQueue[queueName] = new List<string>();
                try
                {
                    if (config.LoadMode == LoadMode.MachinesFromClosestQueue)
                    {
                        // take the first line of this file
                        string closestQueue;
                        while ((closestQueue = queueFileReader.ReadLine()) != null)
                        {
                            closestQueue = closestQueue.Split(',')[0];
                            break;
                        }
                        // and now get some machines
                        var machineFile = Path.Combine(config.QueueToMachineMapDirectory, closestQueue);
                        var machineFileReader = new StreamReader(machineFile);
                        string frequentMachine;
                        while ((frequentMachine = machineFileReader.ReadLine()) != null)
                        {
                            frequentMachine = frequentMachine.Split(',')[0];
                            m_alternativesPerQueue[queueName].Add(frequentMachine);
                            if (m_alternativesPerQueue[queueName].Count == config.QueueToMachineMapInstanceCount)
                            {
                                // done here, there should never be duplicates
                                break;
                            }
                        }
                        // done
                        machineFileReader.Close();
                    }
                    else if (config.LoadMode == LoadMode.NMachinesPerClosestQueues)
                    {
                        // validate this here
                        Contract.Requires(config.QueueInstanceCount > 0, $"QueueInstanceCount has to be positive");
                        Contract.Requires(config.MachinesInstanceCount > 0, $"MachinesInstanceCount has to be positive");
                        // take the first X lines of this file
                        var selectedQueues = new List<string>();
                        string line;
                        while ((line = queueFileReader.ReadLine()) != null)
                        {
                            selectedQueues.Add(line.Split(',')[0]);
                            if (selectedQueues.Count == config.QueueInstanceCount)
                            {
                                break;
                            }
                        }
                        // now, select Y machines per queue
                        foreach (var closeQueue in selectedQueues)
                        {
                            var machineFile = Path.Combine(config.QueueToMachineMapDirectory, closeQueue);
                            var machineFileReader = new StreamReader(machineFile);
                            var added = 0;
                            string frequentMachine;
                            while ((frequentMachine = machineFileReader.ReadLine()) != null)
                            {
                                frequentMachine = frequentMachine.Split(',')[0];
                                if (!m_alternativesPerQueue[queueName].Contains(frequentMachine))
                                {
                                    m_alternativesPerQueue[queueName].Add(frequentMachine);
                                    ++added;
                                }
                                if (added == config.MachinesInstanceCount)
                                {
                                    // done here, there should never be duplicates
                                    break;
                                }
                            }
                            // done
                            machineFileReader.Close();
                        }
                    }
                }
                finally
                {
                    // close this
                    queueFileReader.Close();
                }
            }
            // and we should be done here
        }

        /// <summary>
        ///  Classifies an instance given a RandomForestInstance and a queue name
        /// </summary>
        public ContentPlacementClassifierResult Classify(ContentPlacementInstance instance)
        {
            var forestClassifyResult = m_forest.Classify(instance.Artifact);

            if (!forestClassifyResult.Succeeded)
            {
                return new ContentPlacementClassifierResult(forestClassifyResult);
            }

            return InterpretResult(instance, forestClassifyResult.Value);
        }

        /// <summary>
        ///  Classifies an instance given a RandomForestInstance and a queue name. It uses maxParalellism threads to look up in the random forest
        /// </summary>
        public ContentPlacementClassifierResult Classify(ContentPlacementInstance instance, int maxParalellism)
        {
            var forestClassifyResult = m_forest.Classify(instance.Artifact, maxParalellism);

            if (!forestClassifyResult.Succeeded)
            {
                return new ContentPlacementClassifierResult(forestClassifyResult);
            }

            return InterpretResult(instance, forestClassifyResult.Value);
        }

        private ContentPlacementClassifierResult InterpretResult(ContentPlacementInstance instance, string predictedClass)
        {
            if (predictedClass == s_sharedClassLabel)
            {
                // its shared, so we need to get some alternatives
                if (!m_alternativesPerQueue.ContainsKey(instance.QueueName))
                {
                    return new ContentPlacementClassifierResult(ContentPlacementClassifierResult.ResultCode.QueueNotFound, $"Could not find queue {instance.QueueName}.");
                }
                else
                {
                    if (m_alternativesPerQueue[instance.QueueName].Count > 0)
                    {
                        return new ContentPlacementClassifierResult(m_alternativesPerQueue[instance.QueueName]);
                    }
                    else
                    {
                        return new ContentPlacementClassifierResult(ContentPlacementClassifierResult.ResultCode.ArtifactNotShared, $"Queue {instance.QueueName} has no associated machines.");
                    }
                }
            }
            else
            {
                // done here, do not replicate
                return new ContentPlacementClassifierResult(ContentPlacementClassifierResult.ResultCode.ArtifactNotShared, $"The artifact is not being shared.");
            }
        }
    }

    /// <nodoc />
    public class ContentPlacementInstance
    {
        /// <summary>
        ///  The artifact data used for prediction
        /// </summary>
        public RandomForestInstance Artifact { get; set; }

        /// <summary>
        ///  The attributes we will use here is the queue name
        /// </summary>
        public string QueueName { get; set; }

        /// <nodoc />
        public ContentPlacementInstance() { }

        /// <summary>
        ///  Constructor with attributes for the artifact and the queue name
        /// </summary>
        public ContentPlacementInstance(string queue, double sizeInBytes, double inputPipCount, double outputPipCount,
            double avgPositionInputPips, double avgPositionOutputPips, double avgDepsInputPips, double avgDepsOutputPips,
            double avgInputsInputPips, double avgInputsOutputPips, double avgOutputsInputPips, double avgOutputsOutputPips,
            double avgPriorityInputPips, double avgPriorityOutputPips, double avgWeightInputPips, double avgWeightOutputPips,
            double avgTagCountInputPips, double avgTagCountOutputPips, double avgSemaphoreCountInputPips, double avgSemaphoreCountOutputPips) : this()
        {
            QueueName = queue;
            Artifact = new RandomForestInstance(sizeInBytes, inputPipCount, outputPipCount, avgPositionInputPips,
                avgPositionOutputPips, avgDepsInputPips, avgDepsOutputPips, avgInputsInputPips, avgInputsOutputPips,
                avgOutputsInputPips, avgOutputsOutputPips, avgPriorityInputPips, avgPriorityOutputPips, avgWeightInputPips,
                avgWeightOutputPips, avgTagCountInputPips, avgTagCountOutputPips, avgSemaphoreCountInputPips, avgSemaphoreCountOutputPips);
        }
    }

    /// <summary>
    ///  Represents the configuration for this type of classifier
    /// </summary>
    public sealed class ContentPlacementClassifierConfiguration
    {
        /// <summary>
        ///  Location of the random forest
        /// </summary>
        public string RandomForestFile { get; set; }
        /// <summary>
        ///  Location of the queue to queue (distance) map
        /// </summary>
        public string QueueDistanceMapDirectory { get; set; }
        /// <summary>
        ///  Location of the queue to machine (frequency) map
        /// </summary>
        public string QueueToMachineMapDirectory { get; set; }
        /// <summary>
        ///  How many machines from the queue map will be loaded at startup
        /// </summary>
        public int QueueToMachineMapInstanceCount { get; set; }
        /// <summary>
        ///  If load mode is NMachinesPerClosestQueues, then how many machines per queue will we choose
        /// </summary>
        public int MachinesInstanceCount { get; set; }
        /// <summary>
        ///  If load mode is NMachinesPerClosestQueues, then how many queues we will choose
        /// </summary>
        public int QueueInstanceCount { get; set; }
        /// <summary>
        ///  Load mode
        /// </summary>
        public ContentPlacementClassifier.LoadMode LoadMode { get; set; }
        /// <summary>
        ///  Utility to load configuration from a valid json file
        /// </summary>
        public static ContentPlacementClassifierConfiguration FromJson(string json) => JsonConvert.DeserializeObject<ContentPlacementClassifierConfiguration>(File.ReadAllText(json));

    }

    /// <nodoc />
    public class ContentPlacementClassifierResult : Result<List<string>>
    {
        /// <nodoc />
        public ResultCode ReturnCode { get; set; }

        /// <nodoc />
        public enum ResultCode
        {
            /// <nodoc />
            ArtifactNotShared,
            /// <nodoc />
            NoAlternativesForQueue,
            /// <nodoc />
            QueueNotFound
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(ResultCode code, string errorMessage)
            : base(errorMessage)
        {
            ReturnCode = code;
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(List<string> result) : base(result)
        {
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(List<string> result, bool isNullAllowed) : base(result, isNullAllowed)
        {
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(string errorMessage, string diagnostics = null) : base(errorMessage, diagnostics)
        {
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(Exception exception, string message = null) : base(exception, message)
        {
        }

        /// <nodoc />
        public ContentPlacementClassifierResult(ResultBase other, string message = null) : base(other, message)
        {
        }
    }
}
