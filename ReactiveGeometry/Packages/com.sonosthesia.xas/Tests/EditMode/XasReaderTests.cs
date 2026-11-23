using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Sonosthesia.Xas.Tests
{
    public sealed class XasReaderTests
    {
        private const uint TicksPerSecond = 48000;
        private string _temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "SonosthesiaXasTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
                Directory.Delete(_temporaryDirectory, true);
        }

        [Test]
        public void Reader_InterpolatesAndScrubsAcrossChunkBoundaries()
        {
            string path = XasFixtureWriter.Write(_temporaryDirectory);

            using var reader = new XasReader(path);
            Assert.That(reader.DurationSeconds, Is.EqualTo(1536.0 / TicksPerSecond));
            Assert.That(reader.ContinuousChunkCount, Is.EqualTo(2));
            Assert.That(reader.TryEvaluateContinuous(768.0 / TicksPerSecond, out XasContinuousFrame frame),
                Is.True);
            Assert.That(frame.Rms, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(frame.Centroid, Is.EqualTo(150f).Within(0.01f));

            Assert.That(reader.TryEvaluateContinuous(100.0 / TicksPerSecond, out frame), Is.True);
            Assert.That(frame.Rms, Is.EqualTo(100f / 512f).Within(0.0001f));
            Assert.That(reader.TryEvaluateContinuous(1536.0 / TicksPerSecond, out frame), Is.True);
            Assert.That(frame.Rms, Is.EqualTo(3f));
        }

        [Test]
        public void Reader_QueriesOverlappingPeakEventsIntoCallerBuffer()
        {
            string path = XasFixtureWriter.Write(_temporaryDirectory);
            var destination = new XasPeakEvent[2];

            using var reader = new XasReader(path);
            int total = reader.GetEventCountOverlapping(
                1000.0 / TicksPerSecond,
                1100.0 / TicksPerSecond);
            int written = reader.GetEventsOverlapping(
                1000.0 / TicksPerSecond,
                1100.0 / TicksPerSecond,
                destination);

            Assert.That(total, Is.EqualTo(2));
            Assert.That(written, Is.EqualTo(2));
            Assert.That(destination[0].Channel, Is.EqualTo(XasPeakChannel.Main));
            Assert.That(destination[1].Channel, Is.EqualTo(XasPeakChannel.Mids));
            Assert.That(destination[1].DurationSeconds, Is.Zero);
        }

        [Test]
        public void Reader_CachesChunksWithoutSteadyStateAllocations()
        {
            string path = XasFixtureWriter.Write(_temporaryDirectory);
            var events = new XasPeakEvent[2];

            using var reader = new XasReader(path);
            reader.TryEvaluateContinuous(768.0 / TicksPerSecond, out _);
            reader.GetEventsOverlapping(
                1000.0 / TicksPerSecond,
                1100.0 / TicksPerSecond,
                events);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                reader.TryEvaluateContinuous(
                    (700.0 + i % 100) / TicksPerSecond,
                    out _);
                reader.GetEventsOverlapping(
                    1000.0 / TicksPerSecond,
                    1100.0 / TicksPerSecond,
                    events);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Reader_RejectsCorruptedChunkPayload()
        {
            string path = XasFixtureWriter.Write(_temporaryDirectory);
            XasFixtureWriter.CorruptFirstPayloadByte(path);

            using var reader = new XasReader(path);
            Assert.Throws<InvalidDataException>(() =>
                reader.TryEvaluateContinuous(0, out _));
        }
    }

    internal static class XasFixtureWriter
    {
        private const ushort Major = 1;
        private const uint SampleRate = 48000;
        private const uint Period = 512;
        private const string Descriptor =
            "{\"format\":\"sonosthesia-streaming-audio-analysis\",\"version\":1,"
            + "\"sampleRate\":48000,\"analysisHop\":512,\"duration\":0.032,"
            + "\"timelineOriginSeconds\":0,\"info\":{\"duration\":0.032},"
            + "\"streams\":[{\"id\":0,\"kind\":\"continuous\",\"name\":\"continuous\","
            + "\"firstTick\":0,\"periodTicks\":512,\"interpolation\":\"linear\","
            + "\"encoding\":\"frame-f32-v1\",\"components\":[]},"
            + "{\"id\":1,\"kind\":\"discrete\",\"name\":\"peaks\","
            + "\"encoding\":\"peak-v1\",\"eventType\":\"peak\",\"channels\":[]}]}";

        private static readonly uint[] CrcTable = CreateCrcTable();

        public static string Write(string directory)
        {
            string path = Path.Combine(directory, "fixture.xas");
            byte[] descriptor = Encoding.UTF8.GetBytes(Descriptor);
            var entries = new List<ChunkEntry>();

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(new byte[64]);
            writer.Write(descriptor);
            WritePadding(writer, descriptor.Length);

            WriteContinuousChunk(writer, entries, 0, 0, 1);
            WriteContinuousChunk(writer, entries, 1024, 2, 3);
            WriteEventChunk(writer, entries);

            long indexOffset = stream.Position;
            long entryOffset = indexOffset + 32;
            using (var entryStream = new MemoryStream())
            using (var entryWriter = new BinaryWriter(entryStream, Encoding.UTF8, true))
            {
                foreach (ChunkEntry entry in entries)
                {
                    entryWriter.Write(entry.Kind);
                    entryWriter.Write(entry.Codec);
                    entryWriter.Write((ushort)0);
                    entryWriter.Write(entry.StreamId);
                    entryWriter.Write(entry.ChunkOffset);
                    entryWriter.Write(entry.StoredSize);
                    entryWriter.Write(entry.RecordCount);
                    entryWriter.Write(entry.FirstTick);
                    entryWriter.Write(entry.LastTick);
                    entryWriter.Write(entry.PayloadSize);
                    entryWriter.Write(0u);
                }
                entryWriter.Flush();
                byte[] indexEntries = entryStream.ToArray();

                writer.Write(Encoding.ASCII.GetBytes("XASI"));
                writer.Write(Major);
                writer.Write((ushort)32);
                writer.Write((ushort)48);
                writer.Write((ushort)0);
                writer.Write((uint)entries.Count);
                writer.Write((ulong)entryOffset);
                writer.Write(Crc32C(indexEntries, indexEntries.Length));
                writer.Write(0u);
                writer.Write(indexEntries);
            }

            long fileSize = stream.Position;
            WriteFinalHeader(
                stream,
                writer,
                descriptor,
                (ulong)indexOffset,
                entries.Count,
                (ulong)fileSize);
            writer.Flush();
            return path;
        }

        public static void CorruptFirstPayloadByte(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            stream.Position = 32;
            ulong indexOffset = reader.ReadUInt64();
            stream.Position = (long)indexOffset + 32;
            stream.Position += 8;
            ulong chunkOffset = reader.ReadUInt64();
            stream.Position = (long)chunkOffset + 48;
            int value = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(value ^ 1));
        }

        private static void WriteContinuousChunk(
            BinaryWriter writer,
            List<ChunkEntry> entries,
            ulong firstTick,
            float firstValue,
            float secondValue)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true))
            {
                WriteContinuousFrame(payloadWriter, firstValue);
                WriteContinuousFrame(payloadWriter, secondValue);
                payloadWriter.Flush();
                payload = payloadStream.ToArray();
            }

            WriteChunk(
                writer,
                entries,
                1,
                1,
                0,
                payload,
                2,
                firstTick,
                firstTick + Period);
        }

        private static void WriteContinuousFrame(BinaryWriter writer, float value)
        {
            writer.Write(value);
            writer.Write(value + 10);
            writer.Write(value + 20);
            writer.Write(value + 30);
            writer.Write(value * 100);
        }

        private static void WriteEventChunk(BinaryWriter writer, List<ChunkEntry> entries)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true))
            {
                WritePeak(payloadWriter, 0, 0, 1024, -12, 0.8f);
                WritePeak(payloadWriter, 2, 768, 0, -20, 0.4f);
                payloadWriter.Flush();
                payload = payloadStream.ToArray();
            }

            WriteChunk(writer, entries, 2, 2, 1, payload, 2, 256, 1280);
        }

        private static void WritePeak(
            BinaryWriter writer,
            byte channel,
            uint delta,
            uint duration,
            float magnitude,
            float strength)
        {
            writer.Write(channel);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write(delta);
            writer.Write(duration);
            writer.Write(magnitude);
            writer.Write(strength);
        }

        private static void WriteChunk(
            BinaryWriter writer,
            List<ChunkEntry> entries,
            byte kind,
            byte codec,
            uint streamId,
            byte[] payload,
            uint recordCount,
            ulong firstTick,
            ulong lastTick)
        {
            ulong chunkOffset = (ulong)writer.BaseStream.Position;
            writer.Write(Encoding.ASCII.GetBytes("XASK"));
            writer.Write(kind);
            writer.Write(codec);
            writer.Write((ushort)0);
            writer.Write(streamId);
            writer.Write((ushort)48);
            writer.Write((ushort)0);
            writer.Write((uint)payload.Length);
            writer.Write(recordCount);
            writer.Write(0u);
            writer.Write(firstTick);
            writer.Write(lastTick);
            writer.Write(Crc32C(payload, payload.Length));
            writer.Write(payload);
            WritePadding(writer, payload.Length);

            entries.Add(new ChunkEntry(
                kind,
                codec,
                streamId,
                chunkOffset,
                (uint)(writer.BaseStream.Position - (long)chunkOffset),
                recordCount,
                firstTick,
                lastTick,
                (uint)payload.Length));
        }

        private static void WriteFinalHeader(
            Stream stream,
            BinaryWriter writer,
            byte[] descriptor,
            ulong indexOffset,
            int chunkCount,
            ulong fileSize)
        {
            byte[] header;
            using (var headerStream = new MemoryStream())
            using (var headerWriter = new BinaryWriter(headerStream, Encoding.UTF8, true))
            {
                headerWriter.Write(Encoding.ASCII.GetBytes("XAS1"));
                headerWriter.Write(Major);
                headerWriter.Write((ushort)0);
                headerWriter.Write((ushort)64);
                headerWriter.Write((ushort)3);
                headerWriter.Write(SampleRate);
                headerWriter.Write((ulong)1536);
                headerWriter.Write(64u);
                headerWriter.Write((uint)descriptor.Length);
                headerWriter.Write(indexOffset);
                headerWriter.Write((uint)chunkCount);
                headerWriter.Write(0u);
                headerWriter.Write(fileSize);
                headerWriter.Write(Crc32C(descriptor, descriptor.Length));
                headerWriter.Flush();
                byte[] prefix = headerStream.ToArray();
                header = new byte[64];
                Buffer.BlockCopy(prefix, 0, header, 0, prefix.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(Crc32C(prefix, prefix.Length)), 0, header, 60, 4);
            }

            long current = stream.Position;
            stream.Position = 0;
            writer.Write(header);
            writer.Flush();
            stream.Position = current;
        }

        private static void WritePadding(BinaryWriter writer, int precedingLength)
        {
            int count = (8 - (precedingLength & 7)) & 7;
            if (count > 0)
                writer.Write(new byte[count]);
        }

        private static uint[] CreateCrcTable()
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
            for (int i = 0; i < length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ uint.MaxValue;
        }

        private readonly struct ChunkEntry
        {
            public readonly byte Kind;
            public readonly byte Codec;
            public readonly uint StreamId;
            public readonly ulong ChunkOffset;
            public readonly uint StoredSize;
            public readonly uint RecordCount;
            public readonly ulong FirstTick;
            public readonly ulong LastTick;
            public readonly uint PayloadSize;

            public ChunkEntry(
                byte kind,
                byte codec,
                uint streamId,
                ulong chunkOffset,
                uint storedSize,
                uint recordCount,
                ulong firstTick,
                ulong lastTick,
                uint payloadSize)
            {
                Kind = kind;
                Codec = codec;
                StreamId = streamId;
                ChunkOffset = chunkOffset;
                StoredSize = storedSize;
                RecordCount = recordCount;
                FirstTick = firstTick;
                LastTick = lastTick;
                PayloadSize = payloadSize;
            }
        }
    }
}
