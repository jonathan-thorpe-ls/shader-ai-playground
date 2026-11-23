using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sonosthesia.Xas
{
    public sealed class XasReader : IDisposable
    {
        private const int HeaderSize = 64;
        private const int ChunkHeaderSize = 48;
        private const int IndexHeaderSize = 32;
        private const int IndexEntrySize = 48;

        private const ushort MajorVersion = 1;
        private const ushort MinorVersion = 0;
        private const byte ContinuousKind = 1;
        private const byte DiscreteKind = 2;
        private const byte FrameCodec = 1;
        private const byte PeakCodec = 2;
        private const ushort HasIndexFlag = 1;
        private const ushort FinalizedFlag = 2;
        private const uint KnownHeaderFlags = HasIndexFlag | FinalizedFlag;
        private const uint XasMagic = 0x31534158;
        private const uint IndexMagic = 0x49534158;
        private const uint ChunkMagic = 0x4B534158;

        private static readonly uint[] Crc32CTable = CreateCrc32CTable();

        private readonly FileStream _stream;
        private BinaryReader _reader;
        private readonly bool _verifyCrc;
        private readonly int _cacheChunksPerStream;
        private readonly int _maxDescriptorBytes;
        private readonly int _maxChunkPayloadBytes;
        private XasChunkIndex[] _continuousChunks;
        private XasChunkIndex[] _eventChunks;
        private ContinuousChunkCache[] _continuousCache;
        private EventChunkCache[] _eventCache;
        private byte[] _chunkPayloadBuffer;
        private readonly byte[] _paddingBuffer = new byte[7];
        private long _cacheSequence;
        private bool _disposed;

        public XasReader(string filePath, XasReaderOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A file path is required.", nameof(filePath));

            options ??= XasReaderOptions.Default;
            ValidateOptions(options);

            _verifyCrc = options.VerifyCrc;
            _cacheChunksPerStream = options.CacheChunksPerStream;
            _maxDescriptorBytes = options.MaxDescriptorBytes;
            _maxChunkPayloadBytes = options.MaxChunkPayloadBytes;

            _stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.RandomAccess);

            try
            {
                _reader = new BinaryReader(_stream, Encoding.UTF8, true);
                ReadHeaderAndDescriptor(options.MaxIndexEntries);
                InitializeChunkCaches();
            }
            catch
            {
                _reader?.Dispose();
                _stream.Dispose();
                throw;
            }
        }

        public string FilePath => _stream.Name;
        public ushort Version => MajorVersion;
        public uint TicksPerSecond { get; private set; }
        public double DurationSeconds => (double)DurationTicks / TicksPerSecond;
        public ulong DurationTicks { get; private set; }
        public uint PeriodTicks { get; private set; }
        public int ContinuousChunkCount => _continuousChunks.Length;
        public int EventChunkCount => _eventChunks.Length;
        public int ChunkCount => _continuousChunks.Length + _eventChunks.Length;
        public string DescriptorJson { get; private set; }
        public XasDescriptor Descriptor { get; private set; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _reader.Dispose();
            _stream.Dispose();
            _chunkPayloadBuffer = null;
            _continuousCache = null;
            _eventCache = null;
        }

        public bool TryEvaluateContinuous(double timeSeconds, out XasContinuousFrame frame)
        {
            ThrowIfDisposed();
            frame = default;

            if (!TryTickFromSeconds(timeSeconds, out ulong tick)
                || _continuousChunks.Length == 0
                || tick < _continuousChunks[0].FirstTick
                || tick > _continuousChunks[_continuousChunks.Length - 1].LastTick)
            {
                return false;
            }

            int chunkIndex = FindContinuousChunkAtOrBefore(tick);
            XasChunkIndex chunk = _continuousChunks[chunkIndex];

            if (tick > chunk.LastTick)
            {
                if (chunkIndex + 1 >= _continuousChunks.Length)
                    return false;

                XasChunkIndex next = _continuousChunks[chunkIndex + 1];
                ulong expectedNextTick = AddTicks(chunk.LastTick, PeriodTicks);
                if (expectedNextTick != next.FirstTick)
                    return false;

                if (tick >= next.FirstTick)
                {
                    chunkIndex++;
                    chunk = next;
                }
            }

            ContinuousChunkCache cache = LoadContinuousChunk(chunk);
            ulong offset = tick - chunk.FirstTick;
            int frameIndex = (int)(offset / PeriodTicks);
            uint remainder = (uint)(offset % PeriodTicks);

            if ((uint)frameIndex >= (uint)cache.FrameCount)
                return false;

            XasContinuousFrame left = cache.Frames[frameIndex];
            if (remainder == 0)
            {
                frame = left;
                return true;
            }

            if (frameIndex + 1 < cache.FrameCount)
            {
                frame = XasContinuousFrame.Lerp(
                    left,
                    cache.Frames[frameIndex + 1],
                    (float)remainder / PeriodTicks);
                return true;
            }

            if (chunkIndex + 1 >= _continuousChunks.Length)
                return false;

            XasChunkIndex nextChunk = _continuousChunks[chunkIndex + 1];
            if (nextChunk.FirstTick != AddTicks(chunk.LastTick, PeriodTicks))
                return false;

            ContinuousChunkCache nextCache = LoadContinuousChunk(nextChunk);
            if (nextCache.FrameCount == 0)
                return false;

            frame = XasContinuousFrame.Lerp(
                left,
                nextCache.Frames[0],
                (float)remainder / PeriodTicks);
            return true;
        }

        public int GetEventCountOverlapping(double startSeconds, double endSeconds)
        {
            ThrowIfDisposed();
            if (!TryGetTickRange(startSeconds, endSeconds, out ulong firstTick, out ulong lastTick))
                return 0;

            return GetEventsOverlapping(
                firstTick,
                lastTick,
                Array.Empty<XasPeakEvent>(),
                0,
                0,
                true);
        }

        public int GetEventsOverlapping(
            double startSeconds,
            double endSeconds,
            XasPeakEvent[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            return GetEventsOverlapping(startSeconds, endSeconds, destination, 0, destination.Length);
        }

        public int GetEventsOverlapping(
            double startSeconds,
            double endSeconds,
            XasPeakEvent[] destination,
            int offset,
            int count)
        {
            ThrowIfDisposed();
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if ((uint)offset > (uint)destination.Length
                || count < 0
                || count > destination.Length - offset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "The destination range must fit within the array.");
            }

            if (!TryGetTickRange(startSeconds, endSeconds, out ulong firstTick, out ulong lastTick))
                return 0;

            return GetEventsOverlapping(firstTick, lastTick, destination, offset, count);
        }

        private int GetEventsOverlapping(
            ulong firstTick,
            ulong lastTick,
            XasPeakEvent[] destination,
            int destinationOffset,
            int capacity,
            bool countOnly = false)
        {
            int matchCount = 0;
            for (int chunkIndex = 0; chunkIndex < _eventChunks.Length; chunkIndex++)
            {
                XasChunkIndex chunk = _eventChunks[chunkIndex];
                if (chunk.FirstTick > lastTick)
                    break;
                if (chunk.LastTick < firstTick)
                    continue;

                EventChunkCache cache = LoadEventChunk(chunk);
                for (int eventIndex = 0; eventIndex < cache.EventCount; eventIndex++)
                {
                    XasEventRecord record = cache.Events[eventIndex];
                    ulong eventStart = chunk.FirstTick + record.StartDeltaTicks;
                    ulong eventEnd = eventStart + record.DurationTicks;
                    if (eventStart > lastTick || eventEnd < firstTick)
                        continue;

                    if (!countOnly && matchCount < capacity)
                    {
                        destination[destinationOffset + matchCount] = new XasPeakEvent(
                            (XasPeakChannel)record.Channel,
                            (double)eventStart / TicksPerSecond,
                            (double)record.DurationTicks / TicksPerSecond,
                            record.Magnitude,
                            record.Strength);
                    }

                    matchCount++;
                }
            }

            return countOnly ? matchCount : Math.Min(matchCount, capacity);
        }

        private void ReadHeaderAndDescriptor(int maxIndexEntries)
        {
            if (_stream.Length < HeaderSize)
                throw new InvalidDataException("XAS file is shorter than its fixed header.");

            _stream.Position = 0;
            if (_reader.ReadUInt32() != XasMagic)
                throw new InvalidDataException("Invalid XAS magic.");

            ushort major = _reader.ReadUInt16();
            ushort minor = _reader.ReadUInt16();
            ushort headerSize = _reader.ReadUInt16();
            ushort flags = _reader.ReadUInt16();
            uint ticksPerSecond = _reader.ReadUInt32();
            ulong durationTicks = _reader.ReadUInt64();
            uint descriptorOffset = _reader.ReadUInt32();
            uint descriptorSize = _reader.ReadUInt32();
            ulong indexOffset = _reader.ReadUInt64();
            uint chunkCount = _reader.ReadUInt32();
            uint reserved = _reader.ReadUInt32();
            ulong fileSize = _reader.ReadUInt64();
            uint descriptorCrc = _reader.ReadUInt32();
            uint headerCrc = _reader.ReadUInt32();

            if (major != MajorVersion || minor != MinorVersion)
                throw new NotSupportedException($"Unsupported XAS version {major}.{minor}.");
            if (headerSize != HeaderSize || descriptorOffset != HeaderSize)
                throw new InvalidDataException("Invalid XAS header or descriptor offset.");
            if ((flags & ~KnownHeaderFlags) != 0
                || (flags & HasIndexFlag) == 0
                || (flags & FinalizedFlag) == 0)
            {
                throw new InvalidDataException("The XAS file is not finalized or has unknown flags.");
            }
            if (ticksPerSecond == 0 || reserved != 0)
                throw new InvalidDataException("Invalid XAS header values.");
            if (fileSize != (ulong)_stream.Length || indexOffset > fileSize)
                throw new InvalidDataException("XAS header file bounds are invalid.");
            if (descriptorSize > _maxDescriptorBytes)
                throw new InvalidDataException("XAS descriptor exceeds the configured limit.");
            if (descriptorOffset + (ulong)descriptorSize > indexOffset)
                throw new InvalidDataException("XAS descriptor overlaps the index.");
            if (chunkCount > maxIndexEntries)
                throw new InvalidDataException("XAS index exceeds the configured entry limit.");
            if (_verifyCrc)
            {
                byte[] header = ReadFileRange(0, HeaderSize - 4);
                if (Crc32C(header, header.Length) != headerCrc)
                    throw new InvalidDataException("XAS header CRC-32C mismatch.");
            }

            _stream.Position = descriptorOffset;
            byte[] descriptorBytes = _reader.ReadBytes((int)descriptorSize);
            if (descriptorBytes.Length != descriptorSize)
                throw new EndOfStreamException("Unexpected end of XAS descriptor.");
            if (_verifyCrc && Crc32C(descriptorBytes, descriptorBytes.Length) != descriptorCrc)
                throw new InvalidDataException("XAS descriptor CRC-32C mismatch.");

            DescriptorJson = Encoding.UTF8.GetString(descriptorBytes);
            try
            {
                Descriptor = JsonUtility.FromJson<XasDescriptor>(DescriptorJson);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("The XAS descriptor is not valid JSON.", exception);
            }

            ValidateDescriptor(ticksPerSecond);
            TicksPerSecond = ticksPerSecond;
            DurationTicks = durationTicks;
            PeriodTicks = (uint)Descriptor.analysisHop;
            ReadIndex(indexOffset, chunkCount);
        }

        private void ValidateDescriptor(uint ticksPerSecond)
        {
            if (Descriptor == null
                || Descriptor.format != "sonosthesia-streaming-audio-analysis"
                || Descriptor.version != MajorVersion
                || Descriptor.sampleRate != ticksPerSecond
                || Descriptor.analysisHop <= 0
                || Descriptor.streams == null)
            {
                throw new InvalidDataException("Invalid XAS descriptor header.");
            }

            bool foundContinuous = false;
            bool foundEvents = false;
            for (int i = 0; i < Descriptor.streams.Length; i++)
            {
                XasStreamDescriptor stream = Descriptor.streams[i];
                if (stream.id == 0
                    && stream.kind == "continuous"
                    && stream.encoding == "frame-f32-v1"
                    && stream.periodTicks == Descriptor.analysisHop)
                {
                    foundContinuous = true;
                }
                else if (stream.id == 1
                         && stream.kind == "discrete"
                         && stream.encoding == "peak-v1")
                {
                    foundEvents = true;
                }
            }

            if (!foundContinuous || !foundEvents)
                throw new InvalidDataException("The XAS descriptor lacks required v1 streams.");
        }

        private void ReadIndex(ulong indexOffset, uint chunkCount)
        {
            if (indexOffset > (ulong)_stream.Length - IndexHeaderSize)
                throw new InvalidDataException("XAS index header is outside the file.");

            _stream.Position = (long)indexOffset;
            if (_reader.ReadUInt32() != IndexMagic)
                throw new InvalidDataException("Invalid XAS index magic.");

            ushort major = _reader.ReadUInt16();
            ushort headerSize = _reader.ReadUInt16();
            ushort entrySize = _reader.ReadUInt16();
            ushort flags = _reader.ReadUInt16();
            uint entryCount = _reader.ReadUInt32();
            ulong firstEntryOffset = _reader.ReadUInt64();
            uint indexCrc = _reader.ReadUInt32();
            uint reserved = _reader.ReadUInt32();

            if (major != MajorVersion
                || headerSize != IndexHeaderSize
                || entrySize != IndexEntrySize
                || flags != 0
                || reserved != 0
                || entryCount != chunkCount
                || firstEntryOffset != indexOffset + IndexHeaderSize)
            {
                throw new InvalidDataException("Invalid XAS index header.");
            }

            ulong indexBytes = (ulong)entryCount * IndexEntrySize;
            if (indexBytes > (ulong)_stream.Length - firstEntryOffset)
                throw new InvalidDataException("XAS index entries are truncated.");

            byte[] rawEntries = _reader.ReadBytes((int)indexBytes);
            if (_verifyCrc && Crc32C(rawEntries, rawEntries.Length) != indexCrc)
                throw new InvalidDataException("XAS index CRC-32C mismatch.");

            var allChunks = new XasChunkIndex[entryCount];
            int continuousCount = 0;
            int eventCount = 0;
            int entryOffset = 0;
            ulong previousChunkOffset = 0;

            for (int index = 0; index < entryCount; index++)
            {
                XasChunkIndex chunk = XasChunkIndex.Read(rawEntries, ref entryOffset);
                ValidateIndexEntry(
                    chunk,
                    indexOffset,
                    previousChunkOffset,
                    index);
                allChunks[index] = chunk;
                previousChunkOffset = chunk.ChunkOffset;
                if (chunk.Kind == ContinuousKind)
                    continuousCount++;
                else
                    eventCount++;
            }

            _continuousChunks = new XasChunkIndex[continuousCount];
            _eventChunks = new XasChunkIndex[eventCount];
            int continuousIndex = 0;
            int eventIndex = 0;
            for (int index = 0; index < allChunks.Length; index++)
            {
                XasChunkIndex chunk = allChunks[index];
                if (chunk.Kind == ContinuousKind)
                    _continuousChunks[continuousIndex++] = chunk;
                else
                    _eventChunks[eventIndex++] = chunk;
            }

            SortByFirstTick(_continuousChunks);
            SortByFirstTick(_eventChunks);
        }

        private void ValidateIndexEntry(
            XasChunkIndex chunk,
            ulong indexOffset,
            ulong previousChunkOffset,
            int entryIndex)
        {
            if (chunk.Flags != 0
                || chunk.Reserved != 0
                || chunk.StreamId > 1
                || (chunk.ChunkOffset & 7) != 0
                || chunk.LastTick < chunk.FirstTick
                || chunk.StoredSize < ChunkHeaderSize
                || (entryIndex > 0 && chunk.ChunkOffset <= previousChunkOffset)
                || chunk.ChunkOffset + chunk.StoredSize > indexOffset)
            {
                throw new InvalidDataException($"Invalid XAS index entry {entryIndex}.");
            }

            bool continuous = chunk.Kind == ContinuousKind
                              && chunk.Codec == FrameCodec
                              && chunk.StreamId == 0;
            bool events = chunk.Kind == DiscreteKind
                          && chunk.Codec == PeakCodec
                          && chunk.StreamId == 1;
            int expectedRecordSize = continuous ? 20 : events ? 20 : 0;
            if (expectedRecordSize == 0
                || chunk.PayloadSize != chunk.RecordCount * (uint)expectedRecordSize
                || chunk.PayloadSize > _maxChunkPayloadBytes
                || chunk.StoredSize != Align8(ChunkHeaderSize + (ulong)chunk.PayloadSize))
            {
                throw new InvalidDataException($"Invalid XAS chunk layout at index {entryIndex}.");
            }

            if (continuous
                && (chunk.RecordCount == 0
                    || chunk.LastTick
                    != chunk.FirstTick + (chunk.RecordCount - 1UL) * PeriodTicks))
            {
                throw new InvalidDataException("Invalid continuous XAS chunk timing.");
            }
        }

        private void InitializeChunkCaches()
        {
            int maxContinuousRecords = 0;
            for (int i = 0; i < _continuousChunks.Length; i++)
                maxContinuousRecords = Math.Max(
                    maxContinuousRecords,
                    checked((int)_continuousChunks[i].RecordCount));

            int maxEventRecords = 0;
            int maxPayload = 0;
            for (int i = 0; i < _eventChunks.Length; i++)
            {
                maxEventRecords = Math.Max(
                    maxEventRecords,
                    checked((int)_eventChunks[i].RecordCount));
                maxPayload = Math.Max(maxPayload, checked((int)_eventChunks[i].PayloadSize));
            }

            for (int i = 0; i < _continuousChunks.Length; i++)
                maxPayload = Math.Max(maxPayload, checked((int)_continuousChunks[i].PayloadSize));

            _chunkPayloadBuffer = new byte[maxPayload];
            _continuousCache = new ContinuousChunkCache[_cacheChunksPerStream];
            for (int i = 0; i < _continuousCache.Length; i++)
                _continuousCache[i] = new ContinuousChunkCache(maxContinuousRecords);

            _eventCache = new EventChunkCache[_cacheChunksPerStream];
            for (int i = 0; i < _eventCache.Length; i++)
                _eventCache[i] = new EventChunkCache(maxEventRecords);
        }

        private ContinuousChunkCache LoadContinuousChunk(XasChunkIndex chunk)
        {
            ContinuousChunkCache cache = FindContinuousCache(chunk.ChunkOffset);
            if (cache != null)
                return cache;

            cache = FindOldestContinuousCache();
            byte[] payload = ReadAndValidateChunk(chunk);
            cache.ChunkOffset = chunk.ChunkOffset;
            cache.FrameCount = (int)chunk.RecordCount;
            cache.LastUsed = ++_cacheSequence;

            for (int index = 0; index < cache.FrameCount; index++)
            {
                int offset = index * 20;
                cache.Frames[index] = new XasContinuousFrame(
                    XasBinary.ReadSingle(payload, offset),
                    XasBinary.ReadSingle(payload, offset + 4),
                    XasBinary.ReadSingle(payload, offset + 8),
                    XasBinary.ReadSingle(payload, offset + 12),
                    XasBinary.ReadSingle(payload, offset + 16));
            }

            return cache;
        }

        private EventChunkCache LoadEventChunk(XasChunkIndex chunk)
        {
            EventChunkCache cache = FindEventCache(chunk.ChunkOffset);
            if (cache != null)
                return cache;

            cache = FindOldestEventCache();
            byte[] payload = ReadAndValidateChunk(chunk);
            cache.ChunkOffset = chunk.ChunkOffset;
            cache.EventCount = (int)chunk.RecordCount;
            cache.LastUsed = ++_cacheSequence;

            for (int index = 0; index < cache.EventCount; index++)
            {
                int offset = index * 20;
                if (payload[offset] > (byte)XasPeakChannel.Highs
                    || payload[offset + 1] != 0
                    || payload[offset + 2] != 0
                    || payload[offset + 3] != 0)
                {
                    throw new InvalidDataException("Invalid XAS peak event fields.");
                }
                cache.Events[index] = new XasEventRecord(
                    payload[offset],
                    XasBinary.ReadUInt32(payload, offset + 4),
                    XasBinary.ReadUInt32(payload, offset + 8),
                    XasBinary.ReadSingle(payload, offset + 12),
                    XasBinary.ReadSingle(payload, offset + 16));
            }

            return cache;
        }

        private byte[] ReadAndValidateChunk(XasChunkIndex index)
        {
            if (index.ChunkOffset > (ulong)_stream.Length - ChunkHeaderSize)
                throw new InvalidDataException("XAS chunk header is outside the file.");

            _stream.Position = (long)index.ChunkOffset;
            if (_reader.ReadUInt32() != ChunkMagic
                || _reader.ReadByte() != index.Kind
                || _reader.ReadByte() != index.Codec
                || _reader.ReadUInt16() != 0
                || _reader.ReadUInt32() != index.StreamId
                || _reader.ReadUInt16() != ChunkHeaderSize
                || _reader.ReadUInt16() != 0
                || _reader.ReadUInt32() != index.PayloadSize
                || _reader.ReadUInt32() != index.RecordCount
                || _reader.ReadUInt32() != 0
                || _reader.ReadUInt64() != index.FirstTick
                || _reader.ReadUInt64() != index.LastTick)
            {
                throw new InvalidDataException("XAS chunk header does not match its index.");
            }

            uint payloadCrc = _reader.ReadUInt32();
            ReadExactly(_stream, _chunkPayloadBuffer, (int)index.PayloadSize, "chunk payload");
            if (_verifyCrc
                && Crc32C(_chunkPayloadBuffer, (int)index.PayloadSize) != payloadCrc)
            {
                throw new InvalidDataException("XAS chunk payload CRC-32C mismatch.");
            }

            int paddingSize = (8 - (int)(index.PayloadSize & 7)) & 7;
            ReadExactly(_stream, _paddingBuffer, paddingSize, "chunk padding");
            for (int i = 0; i < paddingSize; i++)
            {
                if (_paddingBuffer[i] != 0)
                    throw new InvalidDataException("XAS chunk padding is not zero-filled.");
            }

            return _chunkPayloadBuffer;
        }

        private ContinuousChunkCache FindContinuousCache(ulong chunkOffset)
        {
            for (int i = 0; i < _continuousCache.Length; i++)
            {
                ContinuousChunkCache cache = _continuousCache[i];
                if (cache.ChunkOffset == chunkOffset)
                {
                    cache.LastUsed = ++_cacheSequence;
                    return cache;
                }
            }

            return null;
        }

        private EventChunkCache FindEventCache(ulong chunkOffset)
        {
            for (int i = 0; i < _eventCache.Length; i++)
            {
                EventChunkCache cache = _eventCache[i];
                if (cache.ChunkOffset == chunkOffset)
                {
                    cache.LastUsed = ++_cacheSequence;
                    return cache;
                }
            }

            return null;
        }

        private ContinuousChunkCache FindOldestContinuousCache()
        {
            ContinuousChunkCache oldest = _continuousCache[0];
            for (int i = 1; i < _continuousCache.Length; i++)
            {
                if (_continuousCache[i].LastUsed < oldest.LastUsed)
                    oldest = _continuousCache[i];
            }

            return oldest;
        }

        private EventChunkCache FindOldestEventCache()
        {
            EventChunkCache oldest = _eventCache[0];
            for (int i = 1; i < _eventCache.Length; i++)
            {
                if (_eventCache[i].LastUsed < oldest.LastUsed)
                    oldest = _eventCache[i];
            }

            return oldest;
        }

        private int FindContinuousChunkAtOrBefore(ulong tick)
        {
            int low = 0;
            int high = _continuousChunks.Length - 1;
            int result = 0;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                if (_continuousChunks[middle].FirstTick <= tick)
                {
                    result = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return result;
        }

        private bool TryTickFromSeconds(double seconds, out ulong tick)
        {
            tick = 0;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return false;

            double rawTick = seconds * TicksPerSecond;
            if (rawTick > ulong.MaxValue)
                return false;

            tick = (ulong)Math.Round(rawTick, MidpointRounding.AwayFromZero);
            return true;
        }

        private bool TryGetTickRange(
            double startSeconds,
            double endSeconds,
            out ulong firstTick,
            out ulong lastTick)
        {
            firstTick = 0;
            lastTick = 0;
            if (!TryTickFromSeconds(startSeconds, out ulong first)
                || !TryTickFromSeconds(endSeconds, out ulong last))
            {
                return false;
            }

            if (last < first)
                (first, last) = (last, first);

            firstTick = first;
            lastTick = last;
            return true;
        }

        private byte[] ReadFileRange(long offset, int length)
        {
            byte[] bytes = new byte[length];
            long current = _stream.Position;
            _stream.Position = offset;
            ReadExactly(_stream, bytes, length, "header range");
            _stream.Position = current;
            return bytes;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count, string field)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException($"Unexpected end of XAS file while reading {field}.");
                offset += read;
            }
        }

        private static ulong Align8(ulong value)
        {
            return (value + 7) & ~7UL;
        }

        private static ulong AddTicks(ulong left, uint right)
        {
            if (left > ulong.MaxValue - right)
                throw new InvalidDataException("XAS tick value overflow.");
            return left + right;
        }

        private static void SortByFirstTick(XasChunkIndex[] chunks)
        {
            Array.Sort(chunks, static (left, right) =>
                left.FirstTick.CompareTo(right.FirstTick));
        }

        private static void ValidateOptions(XasReaderOptions options)
        {
            if (options.CacheChunksPerStream <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(options.CacheChunksPerStream),
                    "At least one cached chunk is required.");
            if (options.MaxDescriptorBytes <= 0
                || options.MaxChunkPayloadBytes <= 0
                || options.MaxIndexEntries <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "XAS limits must be positive.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(XasReader));
        }

        private static uint[] CreateCrc32CTable()
        {
            var table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                uint crc = value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x82F63B78u : 0);
                table[value] = crc;
            }

            return table;
        }

        private static uint Crc32C(byte[] data, int length)
        {
            uint crc = uint.MaxValue;
            for (int index = 0; index < length; index++)
                crc = Crc32CTable[(crc ^ data[index]) & 0xFF] ^ (crc >> 8);
            return crc ^ uint.MaxValue;
        }

        private sealed class ContinuousChunkCache
        {
            public readonly XasContinuousFrame[] Frames;
            public ulong ChunkOffset = ulong.MaxValue;
            public int FrameCount;
            public long LastUsed;

            public ContinuousChunkCache(int capacity)
            {
                Frames = new XasContinuousFrame[capacity];
            }
        }

        private sealed class EventChunkCache
        {
            public readonly XasEventRecord[] Events;
            public ulong ChunkOffset = ulong.MaxValue;
            public int EventCount;
            public long LastUsed;

            public EventChunkCache(int capacity)
            {
                Events = new XasEventRecord[capacity];
            }
        }
    }

    internal readonly struct XasEventRecord
    {
        public readonly byte Channel;
        public readonly uint StartDeltaTicks;
        public readonly uint DurationTicks;
        public readonly float Magnitude;
        public readonly float Strength;

        public XasEventRecord(
            byte channel,
            uint startDeltaTicks,
            uint durationTicks,
            float magnitude,
            float strength)
        {
            Channel = channel;
            StartDeltaTicks = startDeltaTicks;
            DurationTicks = durationTicks;
            Magnitude = magnitude;
            Strength = strength;
        }
    }

    internal readonly struct XasChunkIndex
    {
        public readonly byte Kind;
        public readonly byte Codec;
        public readonly ushort Flags;
        public readonly uint StreamId;
        public readonly ulong ChunkOffset;
        public readonly uint StoredSize;
        public readonly uint RecordCount;
        public readonly ulong FirstTick;
        public readonly ulong LastTick;
        public readonly uint PayloadSize;
        public readonly uint Reserved;

        private XasChunkIndex(
            byte kind,
            byte codec,
            ushort flags,
            uint streamId,
            ulong chunkOffset,
            uint storedSize,
            uint recordCount,
            ulong firstTick,
            ulong lastTick,
            uint payloadSize,
            uint reserved)
        {
            Kind = kind;
            Codec = codec;
            Flags = flags;
            StreamId = streamId;
            ChunkOffset = chunkOffset;
            StoredSize = storedSize;
            RecordCount = recordCount;
            FirstTick = firstTick;
            LastTick = lastTick;
            PayloadSize = payloadSize;
            Reserved = reserved;
        }

        public static XasChunkIndex Read(byte[] bytes, ref int offset)
        {
            XasChunkIndex result = new XasChunkIndex(
                bytes[offset],
                bytes[offset + 1],
                XasBinary.ReadUInt16(bytes, offset + 2),
                XasBinary.ReadUInt32(bytes, offset + 4),
                XasBinary.ReadUInt64(bytes, offset + 8),
                XasBinary.ReadUInt32(bytes, offset + 16),
                XasBinary.ReadUInt32(bytes, offset + 20),
                XasBinary.ReadUInt64(bytes, offset + 24),
                XasBinary.ReadUInt64(bytes, offset + 32),
                XasBinary.ReadUInt32(bytes, offset + 40),
                XasBinary.ReadUInt32(bytes, offset + 44));
            offset += 48;
            return result;
        }
    }

    internal static class XasBinary
    {
        public static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        public static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(
                bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
        }

        public static ulong ReadUInt64(byte[] bytes, int offset)
        {
            return ReadUInt32(bytes, offset)
                   | ((ulong)ReadUInt32(bytes, offset + 4) << 32);
        }

        public static float ReadSingle(byte[] bytes, int offset)
        {
            uint value = ReadUInt32(bytes, offset);
            unsafe
            {
                return *(float*)&value;
            }
        }
    }
}
