using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UpLINE.Line.Transport;

public enum ThriftType : byte
{
    Stop = 0,
    Void = 1,
    Bool = 2,
    Byte = 3,
    Double = 4,
    I16 = 6,
    I32 = 8,
    I64 = 10,
    String = 11,
    Struct = 12,
    Map = 13,
    Set = 14,
    List = 15
}

public sealed class ThriftWriter
{
    private readonly MemoryStream _stream = new();
    private readonly bool _compact;
    private readonly Stack<short> _lastFieldIds = new();
    private bool _pendingBoolField;
    private short _pendingBoolFieldId;

    public ThriftWriter(bool compact = false) => _compact = compact;

    public void WriteMessageBegin(string name, byte messageType = 1, int sequenceId = 0)
    {
        if (_compact)
        {
            WriteRawByte(0x82);
            WriteRawByte((byte)(1 | ((messageType & 0x07) << 5)));
            WriteVarint32((uint)sequenceId);
            WriteString(name);
            return;
        }

        WriteRawI32(unchecked((int)0x80010000 | messageType));
        WriteString(name);
        WriteRawI32(sequenceId);
    }

    public void WriteStructBegin()
    {
        if (_compact) _lastFieldIds.Push(0);
    }

    public void WriteStructEnd()
    {
        if (_compact && _lastFieldIds.Count > 0) _lastFieldIds.Pop();
    }

    public void WriteFieldBegin(ThriftType type, short id)
    {
        if (!_compact)
        {
            WriteRawByte((byte)type);
            WriteRawI16(id);
            return;
        }

        if (type == ThriftType.Bool)
        {
            _pendingBoolField = true;
            _pendingBoolFieldId = id;
            return;
        }

        WriteCompactFieldHeader(type, id);
    }

    public void WriteFieldStop()
    {
        if (_pendingBoolField) throw new InvalidDataException("A compact bool field has no value.");
        WriteRawByte((byte)ThriftType.Stop);
    }

    public void WriteBool(bool value)
    {
        if (_compact && _pendingBoolField)
        {
            WriteCompactFieldHeader(ThriftType.Bool, _pendingBoolFieldId, value);
            _pendingBoolField = false;
            return;
        }
        WriteRawByte(value ? (byte)1 : (byte)0);
    }

    public void WriteByte(byte value) => WriteRawByte(value);

    public void WriteI16(short value)
    {
        if (_compact) WriteVarint32(ZigZag(value));
        else WriteRawI16(value);
    }

    public void WriteI32(int value)
    {
        if (_compact) WriteVarint32(ZigZag(value));
        else WriteRawI32(value);
    }

    public void WriteI64(long value)
    {
        if (_compact) WriteVarint64(ZigZag(value));
        else WriteRawI64(value);
    }

    public void WriteDouble(double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        _stream.Write(bytes);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (_compact) WriteVarint32((uint)bytes.Length);
        else WriteRawI32(bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteBinary(ReadOnlySpan<byte> bytes)
    {
        if (_compact) WriteVarint32((uint)bytes.Length);
        else WriteRawI32(bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteEmptyArgs() => WriteStruct(Array.Empty<(short Id, ThriftType Type, Action<ThriftWriter> Write)>());

    public void WriteStruct(IEnumerable<(short Id, ThriftType Type, Action<ThriftWriter> Write)> fields)
    {
        WriteStructBegin();
        foreach (var field in fields)
        {
            WriteFieldBegin(field.Type, field.Id);
            field.Write(this);
        }
        WriteFieldStop();
        WriteStructEnd();
    }

    public void WriteList<T>(ThriftType itemType, IReadOnlyList<T> values, Action<ThriftWriter, T> writeItem)
    {
        if (_compact)
        {
            var compactType = CompactType(itemType);
            if (values.Count <= 14) WriteRawByte((byte)((values.Count << 4) | compactType));
            else
            {
                WriteRawByte((byte)(0xf0 | compactType));
                WriteVarint32((uint)values.Count);
            }
        }
        else
        {
            WriteRawByte((byte)itemType);
            WriteRawI32(values.Count);
        }

        foreach (var value in values) writeItem(this, value);
    }

    public byte[] ToArray() => _stream.ToArray();

    private void WriteCompactFieldHeader(ThriftType type, short id, bool? boolValue = null)
    {
        var previous = _lastFieldIds.Count == 0 ? (short)0 : _lastFieldIds.Peek();
        var delta = id - previous;
        var compactType = boolValue is true ? (byte)1 : boolValue is false ? (byte)2 : CompactType(type);
        if (delta is >= 1 and <= 15)
            WriteRawByte((byte)((delta << 4) | compactType));
        else
        {
            WriteRawByte(compactType);
            WriteVarint32(ZigZag(id));
        }

        if (_lastFieldIds.Count > 0)
        {
            _lastFieldIds.Pop();
            _lastFieldIds.Push(id);
        }
    }

    private void WriteRawByte(byte value) => _stream.WriteByte(value);

    private void WriteRawI16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        _stream.Write(bytes);
    }

    private void WriteRawI32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        _stream.Write(bytes);
    }

    private void WriteRawI64(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        _stream.Write(bytes);
    }

    private void WriteVarint32(uint value)
    {
        while (value > 0x7f)
        {
            WriteRawByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        WriteRawByte((byte)value);
    }

    private void WriteVarint64(ulong value)
    {
        while (value > 0x7f)
        {
            WriteRawByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        WriteRawByte((byte)value);
    }

    private static uint ZigZag(short value) => (uint)((value << 1) ^ (value >> 15));
    private static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));
    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static byte CompactType(ThriftType type) => type switch
    {
        ThriftType.Bool => 2,
        ThriftType.Byte => 3,
        ThriftType.I16 => 4,
        ThriftType.I32 => 5,
        ThriftType.I64 => 6,
        ThriftType.Double => 7,
        ThriftType.String => 8,
        ThriftType.List => 9,
        ThriftType.Set => 10,
        ThriftType.Map => 11,
        ThriftType.Struct => 12,
        _ => throw new InvalidDataException($"Unsupported compact Thrift type {type}.")
    };
}

public readonly record struct ThriftField(ThriftType Type, short Id);

public sealed class ThriftStruct
{
    private readonly Dictionary<short, object?> _fields = new();

    public IReadOnlyDictionary<short, object?> Fields => _fields;
    public void Set(short id, object? value) => _fields[id] = value;
    public object? Value(short id) => _fields.TryGetValue(id, out var value) ? value : null;
    public string? String(short id) => Value(id) as string;
    public int? Int32(short id) => Value(id) is int number ? number : null;
    public long? Int64(short id) => Value(id) is long number ? number : null;
    public bool? Bool(short id) => Value(id) is bool boolean ? boolean : null;
    public ThriftStruct? Struct(short id) => Value(id) as ThriftStruct;

    public IReadOnlyDictionary<string, string> StringMap(short id)
    {
        if (Value(id) is not Dictionary<object, object?> map)
            return new Dictionary<string, string>();
        return map.Where(pair => pair.Key is string && pair.Value is string)
            .ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value!);
    }
}

public sealed class ThriftReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly bool _compact;
    private readonly Stack<short> _lastFieldIds = new();
    private int _offset;
    private bool? _pendingCompactBool;

    public ThriftReader(ReadOnlyMemory<byte> data, bool compact = false)
    {
        _data = data;
        _compact = compact;
    }

    public (string Name, byte MessageType, int SequenceId) ReadMessageBegin()
    {
        if (_compact)
        {
            if (ReadByte() != 0x82) throw new InvalidDataException("Unsupported compact Thrift protocol.");
            var versionAndType = ReadByte();
            if ((versionAndType & 0x1f) != 1) throw new InvalidDataException("Unsupported compact Thrift version.");
            var sequenceId = checked((int)ReadVarint64());
            var name = ReadString();
            return (name, (byte)((versionAndType >> 5) & 0x07), sequenceId);
        }

        var binaryVersionAndType = ReadRawI32();
        if ((binaryVersionAndType & unchecked((int)0xffff0000)) != unchecked((int)0x80010000))
            throw new InvalidDataException("Unsupported Thrift message version.");
        var binaryName = ReadString();
        var binarySequenceId = ReadRawI32();
        return (binaryName, (byte)(binaryVersionAndType & 0xff), binarySequenceId);
    }

    public ThriftStruct ReadStruct()
    {
        if (_compact) _lastFieldIds.Push(0);
        var result = new ThriftStruct();
        while (true)
        {
            var field = ReadFieldBegin();
            if (field.Type == ThriftType.Stop)
            {
                if (_compact && _lastFieldIds.Count > 0) _lastFieldIds.Pop();
                return result;
            }
            result.Set(field.Id, ReadValue(field.Type));
        }
    }

    public ThriftField ReadFieldBegin()
    {
        if (!_compact)
        {
            var type = (ThriftType)ReadByte();
            if (type == ThriftType.Stop) return new ThriftField(type, 0);
            return new ThriftField(type, ReadRawI16());
        }

        var header = ReadByte();
        var compactType = (byte)(header & 0x0f);
        if (compactType == 0) return new ThriftField(ThriftType.Stop, 0);
        var previous = _lastFieldIds.Count == 0 ? (short)0 : _lastFieldIds.Peek();
        var fieldId = (short)((header >> 4) == 0 ? ReadZigZag32() : previous + (header >> 4));
        if (_lastFieldIds.Count > 0)
        {
            _lastFieldIds.Pop();
            _lastFieldIds.Push(fieldId);
        }
        if (compactType is 1 or 2)
        {
            _pendingCompactBool = compactType == 1;
            return new ThriftField(ThriftType.Bool, fieldId);
        }
        return new ThriftField(FromCompactType(compactType), fieldId);
    }

    public object? ReadValue(ThriftType type)
    {
        if (_compact && type == ThriftType.Bool && _pendingCompactBool is bool compactBool)
        {
            _pendingCompactBool = null;
            return compactBool;
        }

        return type switch
        {
            ThriftType.Bool => ReadByte() != 0,
            ThriftType.Byte => ReadByte(),
            ThriftType.I16 => _compact ? (short)ReadZigZag32() : ReadRawI16(),
            ThriftType.I32 => _compact ? ReadZigZag32() : ReadRawI32(),
            ThriftType.I64 => _compact ? ReadZigZag64() : ReadRawI64(),
            ThriftType.Double => ReadDouble(),
            ThriftType.String => ReadString(),
            ThriftType.Struct => ReadStruct(),
            ThriftType.Map => ReadMap(),
            ThriftType.List or ThriftType.Set => ReadList(),
            _ => throw new InvalidDataException($"Unsupported Thrift type {type}.")
        };
    }

    public void Skip(ThriftType type) => _ = ReadValue(type);

    private Dictionary<object, object?> ReadMap()
    {
        var count = _compact ? checked((int)ReadVarint64()) : ReadRawI32();
        if (count < 0 || count > 100_000) throw new InvalidDataException("Invalid map length.");
        if (count == 0) return new Dictionary<object, object?>();
        var types = ReadByte();
        ThriftType keyType;
        ThriftType valueType;
        if (_compact)
        {
            keyType = FromCompactType((byte)(types >> 4));
            valueType = FromCompactType((byte)(types & 0x0f));
        }
        else
        {
            keyType = (ThriftType)types;
            valueType = (ThriftType)ReadByte();
        }
        var map = new Dictionary<object, object?>();
        for (var i = 0; i < count; i++) map[ReadValue(keyType)!] = ReadValue(valueType);
        return map;
    }

    private List<object?> ReadList()
    {
        ThriftType itemType;
        int count;
        if (_compact)
        {
            var header = ReadByte();
            var compactType = (byte)(header & 0x0f);
            count = header >> 4;
            if (count == 15) count = checked((int)ReadVarint64());
            itemType = FromCompactType(compactType);
        }
        else
        {
            itemType = (ThriftType)ReadByte();
            count = ReadRawI32();
        }
        if (count < 0 || count > 100_000) throw new InvalidDataException("Invalid collection length.");
        var list = new List<object?>(count);
        for (var i = 0; i < count; i++) list.Add(ReadValue(itemType));
        return list;
    }

    private byte ReadByte()
    {
        Ensure(1);
        return _data.Span[_offset++];
    }

    private short ReadRawI16()
    {
        Ensure(2);
        var value = BinaryPrimitives.ReadInt16BigEndian(_data.Span.Slice(_offset, 2));
        _offset += 2;
        return value;
    }

    private int ReadRawI32()
    {
        Ensure(4);
        var value = BinaryPrimitives.ReadInt32BigEndian(_data.Span.Slice(_offset, 4));
        _offset += 4;
        return value;
    }

    private long ReadRawI64()
    {
        Ensure(8);
        var value = BinaryPrimitives.ReadInt64BigEndian(_data.Span.Slice(_offset, 8));
        _offset += 8;
        return value;
    }

    private double ReadDouble()
    {
        Ensure(8);
        var value = BinaryPrimitives.ReadInt64LittleEndian(_data.Span.Slice(_offset, 8));
        _offset += 8;
        return BitConverter.Int64BitsToDouble(value);
    }

    private string ReadString()
    {
        var length = _compact ? checked((int)ReadVarint64()) : ReadRawI32();
        if (length < 0 || length > 10_000_000) throw new InvalidDataException("Invalid string length.");
        Ensure(length);
        var value = Encoding.UTF8.GetString(_data.Span.Slice(_offset, length));
        _offset += length;
        return value;
    }

    private uint ReadVarint32() => checked((uint)ReadVarint64());

    private ulong ReadVarint64()
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            var current = ReadByte();
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("Invalid compact varint.");
        }
    }

    private int ReadZigZag32()
    {
        var value = ReadVarint32();
        return (int)((value >> 1) ^ (uint)-(int)(value & 1));
    }

    private long ReadZigZag64()
    {
        var value = ReadVarint64();
        return (long)((value >> 1) ^ (ulong)-(long)(value & 1));
    }

    private void Ensure(int count)
    {
        if (count < 0 || _offset > _data.Length - count) throw new EndOfStreamException();
    }

    private static ThriftType FromCompactType(byte type) => type switch
    {
        1 or 2 => ThriftType.Bool,
        3 => ThriftType.Byte,
        4 => ThriftType.I16,
        5 => ThriftType.I32,
        6 => ThriftType.I64,
        7 => ThriftType.Double,
        8 => ThriftType.String,
        9 => ThriftType.List,
        10 => ThriftType.Set,
        11 => ThriftType.Map,
        12 => ThriftType.Struct,
        _ => throw new InvalidDataException($"Unsupported compact Thrift type {type}.")
    };
}
