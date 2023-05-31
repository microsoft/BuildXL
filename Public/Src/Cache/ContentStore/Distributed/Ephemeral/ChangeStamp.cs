// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// A number that can only grow monotonically until it reaches <see cref="MaxValue"/>.
/// </summary>
public readonly record struct SequenceNumber : IComparable<SequenceNumber>, IComparable
{
    private readonly uint _sequenceNumber;

    /// <summary>
    /// The minimum allowed value for a <see cref="SequenceNumber"/>.
    /// </summary>
    public static readonly SequenceNumber MinValue = new(0);

    /// <summary>
    /// The maximum allowed value for a <see cref="SequenceNumber"/>.
    /// </summary>
    /// <remarks>
    /// This number can only grow up to <see cref="ChangeStamp.MaxSequenceNumber"/> because it is packed inside of
    /// <see cref="ChangeStamp"/>.
    /// </remarks>
    public static readonly SequenceNumber MaxValue = new((uint)ChangeStamp.MaxSequenceNumber);

    public SequenceNumber(uint sequenceNumber)
    {
        Contract.Requires(sequenceNumber <= ChangeStamp.MaxSequenceNumber, $"The maximum allowed value for {nameof(SequenceNumber)} is {MaxValue}");
        _sequenceNumber = sequenceNumber;
    }

    public SequenceNumber Next()
    {
        return new(_sequenceNumber + 1);
    }

    public override string ToString()
    {
        return _sequenceNumber.ToString();
    }

    public static implicit operator uint(SequenceNumber sequenceNumber)
    {
        return sequenceNumber._sequenceNumber;
    }

    public int CompareTo(SequenceNumber other)
    {
        return _sequenceNumber.CompareTo(other._sequenceNumber);
    }

    public int CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            SequenceNumber other => CompareTo(other),
            _ => throw new ArgumentException($"Object must be of type {nameof(SequenceNumber)}")
        };
    }

    public static bool operator <(SequenceNumber left, SequenceNumber right) => left.CompareTo(right) < 0;
    public static bool operator <=(SequenceNumber left, SequenceNumber right) => left.CompareTo(right) <= 0;
    public static bool operator >(SequenceNumber left, SequenceNumber right) => left.CompareTo(right) > 0;
    public static bool operator >=(SequenceNumber left, SequenceNumber right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// The objective of this class is to have a single abstraction that can be used to determine the Happens Before
/// relation inside <see cref="LastWriterWinsSet{T}"/>.
///
/// Since we need to store a lot of these (one per each piece of content in each machine), this class aims to reduce
/// space utilization as much as possible, hence why we this class packs several things into <see cref="RawData"/>:
/// - <see cref="OperationBits"/> bits for the <see cref="Operation"/>
/// - <see cref="SequenceNumberBits"/> bits for the <see cref="SequenceNumber"/>
/// - <see cref="TimestampBits"/> bits for the <see cref="TimestampUtc"/>
///
/// Please note, this does imply that each one of these components is limited in range. See their documentation below
/// for more information.
/// </summary>
public readonly record struct ChangeStamp : IComparable<ChangeStamp>, IComparable
{
    /// <summary>
    /// The Invalid value.
    /// </summary>
    /// <remarks>
    /// This value is chosen on purpose because it is impossible to represent: since the <see cref="EpochUtc"/> is in
    /// the past at the time of writing, all valid timestamps will be greater than this value.
    /// </remarks>
    public static ChangeStamp Invalid { get; } = new() { RawData = 0 };

    /// <summary>
    /// Binary representation of the <see cref="ChangeStamp"/>. This packs the information as follows:
    ///     [Operation][SequenceNumber][TimestampUtc]
    /// </summary>
    public required long RawData { get; init; }

    public ChangeStampOperation Operation => ExtractOperation(RawData);

    public SequenceNumber SequenceNumber => ExtractSequenceNumber(RawData);

    public DateTime TimestampUtc => ExtractTimestamp(RawData);

    /// <summary>
    /// Packs the information into a <see cref="long"/>. This is the inverse of <see cref="RawData"/>.
    /// </summary>
    public static ChangeStamp Create(SequenceNumber sequenceNumber, DateTime timestampUtc, ChangeStampOperation operation)
    {
        Contract.Requires(timestampUtc >= EpochUtc, $"Attempt to represent a timestamp before the epoch ({EpochUtc}): {timestampUtc}");
        Contract.Requires(timestampUtc <= MaxTimestampUtc, $"Attempt to represent a timestamp past the maximum value ({MaxTimestampUtc}): {timestampUtc}");

        return new ChangeStamp { RawData = Pack(sequenceNumber, timestampUtc, operation) };
    }

    /// <summary>
    /// Compute the next valid <see cref="ChangeStamp"/>
    /// </summary>
    public ChangeStamp Next(ChangeStampOperation operation, DateTime now)
    {
        return Create(SequenceNumber.Next(), now, operation);
    }

    /// <summary>
    /// Because we have only <see cref="TimestampBits"/> to fit a <see cref="DateTime"/>, which is 64 bits, we decrease
    /// the resolution of the timestamp to milliseconds. Moreover, we also shift the epoch to the future to allow a
    /// larger range of values to be represented.
    /// </summary>
    internal static readonly DateTime EpochUtc = new(2023, 01, 01, 00, 00, 00, DateTimeKind.Utc);

    /// <summary>
    /// Number of bits reserved for the <see cref="Operation"/>
    /// </summary>
    private const int OperationBits = 4;

    /// <summary>
    /// Number of bits reserved for the <see cref="SequenceNumber"/>
    /// </summary>
    private const int SequenceNumberBits = 19;

    /// <summary>
    /// Number of bits reserved for the <see cref="TimestampUtc"/>
    /// </summary>
    private const int TimestampBits = 41;

    internal const long OperationMask = (1L << OperationBits) - 1;
    internal const long SequenceNumberMask = (1L << SequenceNumberBits) - 1;
    internal const long TimestampMask = (1L << TimestampBits) - 1;

    /// <summary>
    /// Maximum representable value for <see cref="Operation"/>.
    /// </summary>
    public const long MaxOperation = OperationMask;

    /// <summary>
    /// Maximum representable value for <see cref="SequenceNumber"/>.
    /// </summary>
    public const long MaxSequenceNumber = SequenceNumberMask;

    /// <summary>
    /// Maximum representable value for <see cref="TimestampUtc"/>.
    /// </summary>
    public static DateTime MaxTimestampUtc => EpochUtc.AddMilliseconds(TimestampMask);

    private static long Pack(uint sequenceNumber, DateTime timestampUtc, ChangeStampOperation operation)
    {
        long value = 0;

        value |= ((long)operation & OperationMask) << (SequenceNumberBits + TimestampBits);

        value |= (sequenceNumber & SequenceNumberMask) << TimestampBits;

        long timestampMilliseconds = (timestampUtc.Ticks - EpochUtc.Ticks) / TimeSpan.TicksPerMillisecond;
        value |= (timestampMilliseconds & TimestampMask) << 0;

        return value;
    }

    private static ChangeStampOperation ExtractOperation(long instance)
    {
        return (ChangeStampOperation)((instance >> (SequenceNumberBits + TimestampBits)) & OperationMask);
    }

    private static SequenceNumber ExtractSequenceNumber(long instance)
    {
        var sequenceNumber = (uint)((instance >> TimestampBits) & SequenceNumberMask);
        return new(sequenceNumber);
    }

    private static DateTime ExtractTimestamp(long instance)
    {
        long timestampMilliseconds = (instance >> 0) & TimestampMask;
        long timestampTicks = (timestampMilliseconds * TimeSpan.TicksPerMillisecond) + EpochUtc.Ticks;

        return new DateTime(timestampTicks, DateTimeKind.Utc);
    }

    public int CompareTo(ChangeStamp other)
    {
        int sequenceNumberComparison = SequenceNumber.CompareTo(other.SequenceNumber);
        if (sequenceNumberComparison != 0)
        {
            return sequenceNumberComparison;
        }

        int timestampComparison = TimestampUtc.CompareTo(other.TimestampUtc);
        if (timestampComparison != 0)
        {
            return timestampComparison;
        }

        return Operation.CompareTo(other.Operation);
    }

    public int CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            ChangeStamp other => CompareTo(other),
            _ => throw new ArgumentException($"Object must be of type {nameof(ChangeStamp)}, found {obj.GetType()} instead")
        };
    }

    public static bool operator <(ChangeStamp left, ChangeStamp right) => left.CompareTo(right) < 0;
    public static bool operator <=(ChangeStamp left, ChangeStamp right) => left.CompareTo(right) <= 0;
    public static bool operator >(ChangeStamp left, ChangeStamp right) => left.CompareTo(right) > 0;
    public static bool operator >=(ChangeStamp left, ChangeStamp right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        return $"{Operation}<{SequenceNumber}>{TimestampUtc}";
    }
}

/// <summary>
/// Operations that can be stored inside a <see cref="ChangeStamp"/>.
/// </summary>
/// <remarks>
/// The maximum value achieved by this enum MUST be less than <see cref="ChangeStamp.MaxOperation"/>.
/// </remarks>
public enum ChangeStampOperation
{
    Add = 0,
    Delete = 1,
}
