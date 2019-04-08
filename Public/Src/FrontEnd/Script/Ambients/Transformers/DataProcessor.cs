// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Data processor.
    /// </summary>
    /// <remarks>
    /// Data that can be processed has the following recursive definitions:
    ///     Data = int | string | PathAtom | RelativePath | AbsolutePath | CompoundData
    ///     CompoundData = { separator: string, contents: Data[] }
    /// </remarks>
    internal sealed class DataProcessor
    {
        private readonly ObjectPool<PipDataBuilder> m_pipDataBuilderPool;
        private readonly ImmutableContextBase m_context;

        private DataProcessor(ImmutableContextBase context, ObjectPool<PipDataBuilder> pipDataBuilderPool)
        {
            Contract.Requires(context != null);
            Contract.Requires(pipDataBuilderPool != null);

            m_context = context;
            m_pipDataBuilderPool = pipDataBuilderPool;
        }

        /// <summary>
        /// Processes data.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <param name="pipDataBuilderPool">Pool for PipDataBuilders.</param>
        /// <param name="data">Data to be processed.</param>
        /// <param name="conversionContext">Conversion context.</param>
        /// <returns>Pip data.</returns>
        public static PipData ProcessData(
            ImmutableContextBase context,
            ObjectPool<PipDataBuilder> pipDataBuilderPool,
            EvaluationResult data,
            in ConversionContext conversionContext = default(ConversionContext))
        {
            Contract.Requires(context != null);
            Contract.Requires(pipDataBuilderPool != null);
            Contract.Requires(data != null);

            return new DataProcessor(context, pipDataBuilderPool).ProcessData(data, conversionContext);
        }

        private PipData ProcessData(EvaluationResult data, in ConversionContext conversionContext)
        {
            Contract.Requires(data != null);

            using (var pipDataBuilderWrapper = m_pipDataBuilderPool.GetInstance())
            {
                var pipDataBuilder = pipDataBuilderWrapper.Instance;
                PopulateDataIntoPipDataBuilder(data, pipDataBuilder, conversionContext);
                return pipDataBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
            }
        }

        private void PopulateDataIntoPipDataBuilder(EvaluationResult result, PipDataBuilder pipDataBuilder, in ConversionContext conversionContext)
        {
            Contract.Requires(pipDataBuilder != null);

            object data = result.Value;

            if (data is string stringData)
            {
                pipDataBuilder.Add(stringData);
            }
            else if (data is IImplicitPath pathData)
            {
                pipDataBuilder.Add(pathData.Path);
            }
            else if (data is ObjectLiteral compoundData)
            {
                var separator = Converter.ExpectString(
                    compoundData[m_context.Names.DataSeparator],
                    new ConversionContext(name: m_context.Names.DataSeparator, allowUndefined: true, objectCtx: compoundData));
                separator = separator ?? Environment.NewLine;
                var arrayOfData = Converter.ExtractArrayLiteral(compoundData, m_context.Names.DataContents);
                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, separator))
                {
                    for (int i = 0; i < arrayOfData.Length; ++i)
                    {
                        PopulateDataIntoPipDataBuilder(arrayOfData[i], pipDataBuilder, new ConversionContext(pos: i, allowUndefined: false, objectCtx: arrayOfData));
                    }
                }
            }
            else if (data is PathAtom)
            {
                pipDataBuilder.Add((PathAtom)data);
            }
            else if (data is RelativePath)
            {
                pipDataBuilder.Add(((RelativePath)data).ToString(m_context.StringTable));
            }
            else if (data is int)
            {
                pipDataBuilder.Add(((int)data).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                throw Converter.CreateException(
                    new[] { typeof(string), typeof(AbsolutePath), typeof(PathAtom), typeof(RelativePath), typeof(int), typeof(ObjectLiteral) },
                    result,
                    conversionContext);
            }
        }
    }
}
