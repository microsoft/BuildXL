using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ContentPlacementAnalysisTools.Core.ML.Classifier
{
    /// <summary>
    ///  Base class for classifiers
    /// </summary>
    public abstract class MLClassifier
    {

        /// <summary>
        ///  Classify a single instance
        /// </summary>
        public abstract void Classify(MLInstance instance);
        /// <summary>
        ///  Classify a single instance using many threads
        /// </summary>
        public abstract void Classify(MLInstance instance, int maxParalellism);

    }

    /// <summary>
    ///  An instance to be classified
    /// </summary>
    public abstract class MLInstance
    {
        /// <summary>
        ///  The class set after the prediction is done
        /// </summary>
        public string PredictedClass { get; set; } = null;
        /// <summary>
        ///  The attributes to be used for prediction
        /// </summary>
        public Dictionary<string, double> Attributes { get; set; } = new Dictionary<string, double>();
    }
}
