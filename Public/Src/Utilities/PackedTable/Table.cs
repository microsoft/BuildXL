// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Base abstract Table class with some common implementation.
    /// </summary>
    public abstract class Table<TId, TValue> : ITable<TId>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// Arbitrary default table capacity.
        /// </summary>
        protected const int DefaultCapacity = 100;

        /// <summary>
        /// Construct a Table.
        /// </summary>
        public Table(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) { throw new ArgumentException($"Capacity {capacity} must be >= 0"); }
            SingleValues = new SpannableList<TValue>(capacity);
        }

        /// <summary>
        /// Construct a derived Table sharing the IDs of the given base Table.
        /// </summary>
        public Table(ITable<TId> baseTable)
        {
            if (baseTable == null) { throw new ArgumentException("Base table must not be null"); }
            BaseTableOpt = baseTable;
            SingleValues = new SpannableList<TValue>(baseTable.Count == 0 ? DefaultCapacity : baseTable.Count);
        }

        /// <summary>
        /// List of values; index in list + 1 = ID of value.
        /// </summary>
        protected SpannableList<TValue> SingleValues;

        /// <summary>
        /// Size of the Table's backing store.
        /// </summary>
        public int Capacity
        {
            get => SingleValues.Capacity;
            set => SingleValues.Capacity = value;
        }

        /// <summary>
        /// The number of IDs stored in this table.
        /// </summary>
        public int Count => SingleValues.Count;

        /// <summary>
        /// Return the current range of defined IDs.
        /// </summary>
        /// <remarks>
        /// Mainly useful for testing.
        /// </remarks>
        public IEnumerable<TId> Ids => Enumerable.Range(1, Count).Select(v => default(TId).ToId(v));

        /// <summary>
        /// The base table (if any) that defines this table's ID range.
        /// </summary>
        /// <remarks>
        /// "Opt" signifies "optional". All IDs added to this table must be within the ID range of BaseTableOpt, if it exists.
        /// </remarks>
        public ITable<TId> BaseTableOpt { get; private set; }

        /// <summary>
        /// Is the given ID valid for this table's current size?
        /// </summary>
        public bool IsValid(TId id)
        {
            return id.FromId() > 0 && id.FromId() <= Count;
        }

        /// <summary>
        /// Check that the given ID is valid and throw ArgumentException if not.
        /// </summary>
        /// <param name="id"></param>
        public void CheckValid(TId id)
        {
            if (!IsValid(id)) { throw new ArgumentException($"ID {id} is not valid for table with Count {Count}"); }
        }

        /// <summary>
        /// Fill this table with default values, to the same count as the base table.
        /// </summary>
        /// <remarks>
        /// This method throws an exception if called on a table with no BaseTableOpt value.
        /// 
        /// The purpose of this method is to support the scenario in which a base table gets fully populated before
        /// a derived table, and the derived table is going to be populated in random order, hence needs to have
        /// a full count of initial (default-valued) values.
        /// </remarks>
        public virtual void FillToBaseTableCount()
        {
            if (BaseTableOpt == null) { throw new ArgumentException("FillToBaseTableCount() must only be called on tables with a base table (e.g. non-null BaseTableOpt)"); }

            if (BaseTableOpt.Count > Count)
            {
                SingleValues.Fill(BaseTableOpt.Count - Count, default);
            }
        }

        /// <summary>
        /// Save to file.
        /// </summary>
        public virtual void SaveToFile(string directory, string name)
        {
            FileSpanUtilities.SaveToFile(directory, name, SingleValues);
        }

        /// <summary>
        /// Load from file.
        /// </summary>
        public virtual void LoadFromFile(string directory, string name)
        {
            FileSpanUtilities.LoadFromFile(directory, name, SingleValues);
        }

        /// <summary>
        /// Add the given suffix to the filename, preserving extension.
        /// </summary>
        /// <remarks>
        /// This is used by save and load methods in derived types.
        /// 
        /// For example, if this table is a MultiValuesTable and path is "foo.bin",
        /// the derived implementation may call this with "foo.bin" and "MultiValues",
        /// and will get "foo.MultiValues.bin" back.
        /// </remarks>
        protected string InsertSuffix(string path, string suffix)
        {
            int lastDot = path.LastIndexOf('.');
            return $"{path.Substring(0, lastDot)}.{suffix}{path.Substring(lastDot)}";
        }
    }
}
