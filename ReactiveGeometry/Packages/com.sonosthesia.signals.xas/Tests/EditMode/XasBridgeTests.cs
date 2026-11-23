using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sonosthesia.Signals;
using UnityEngine;

namespace Sonosthesia.Signals.Xas.Tests
{
    public sealed class XasBridgeTests
    {
        private string _relativePath;
        private string _absolutePath;

        [SetUp]
        public void SetUp()
        {
            string directory = Path.Combine(
                Application.streamingAssetsPath,
                "Phase7Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _absolutePath = Path.Combine(directory, "fixture.xas");
            _relativePath = _absolutePath.Substring(
                Application.streamingAssetsPath.Length + 1);
            XasBridgeFixture.Write(_absolutePath);
        }

        [TearDown]
        public void TearDown()
        {
            string directory = Path.GetDirectoryName(_absolutePath);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void Blend_NormalizesContinuousWeightsAndMergesPulses()
        {
            var sums = new float[2];
            var weights = new float[2];
            var available = new byte[2];
            var pulseStrengths = new float[1];
            var pulseMagnitudes = new float[1];
            var pulseStarted = new byte[1];

            XasSignalBlend.AddContinuous(
                new[] { 10f, 20f },
                new byte[] { 1, 1 },
                0.25f,
                sums,
                weights,
                2);
            XasSignalBlend.AddContinuous(
                new[] { 20f, 40f },
                new byte[] { 1, 1 },
                0.75f,
                sums,
                weights,
                2);
            XasSignalBlend.FinishContinuous(sums, weights, available, 2);
            XasSignalBlend.AddPulses(
                new[] { 0.4f },
                new[] { -20f },
                new byte[] { 0 },
                0.5f,
                pulseStrengths,
                pulseMagnitudes,
                pulseStarted,
                1);
            XasSignalBlend.AddPulses(
                new[] { 0.8f },
                new[] { -12f },
                new byte[] { 1 },
                1f,
                pulseStrengths,
                pulseMagnitudes,
                pulseStarted,
                1);

            Assert.That(sums[0], Is.EqualTo(17.5f).Within(0.001f));
            Assert.That(sums[1], Is.EqualTo(35f).Within(0.001f));
            Assert.That(available, Is.EqualTo(new byte[] { 1, 1 }));
            Assert.That(pulseStrengths[0], Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(pulseMagnitudes[0], Is.EqualTo(-12f));
            Assert.That(pulseStarted[0], Is.EqualTo(1));
        }

        [Test]
        public void Provider_MapsContinuousAndPeakDataToGenericProfile()
        {
            XasAnalysisAsset asset = ScriptableObject.CreateInstance<XasAnalysisAsset>();
            asset.Configure(_relativePath, "Fixture", 512.0 / 48000.0);
            using var provider = new XasControlSignalProvider(asset);
            var continuous = new float[XasControlSignalProfile.ContinuousCount];
            var available = new byte[XasControlSignalProfile.ContinuousCount];
            var strengths = new float[XasControlSignalProfile.PulseCount];
            var magnitudes = new float[XasControlSignalProfile.PulseCount];
            var started = new byte[XasControlSignalProfile.PulseCount];

            bool sampled = provider.TrySample(
                256.0 / 48000.0,
                0,
                true,
                continuous,
                available,
                strengths,
                magnitudes,
                started);

            Assert.That(sampled, Is.True);
            Assert.That(provider.Profile, Is.SameAs(XasControlSignalProfile.Instance));
            Assert.That(continuous[0], Is.EqualTo(-15f).Within(0.01f));
            Assert.That(available, Has.All.EqualTo(1));
            Assert.That(strengths[0], Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(magnitudes[0], Is.EqualTo(-12f).Within(0.01f));
            Assert.That(started[0], Is.EqualTo(1));
        }
    }

    internal static class XasBridgeFixture
    {
        private const uint SampleRate = 48000;
        private static readonly uint[] CrcTable = CreateCrcTable();
        private const string Descriptor =
            "{\"format\":\"sonosthesia-streaming-audio-analysis\",\"version\":1,"
            + "\"sampleRate\":48000,\"analysisHop\":512,\"duration\":0.0106666667,"
            + "\"timelineOriginSeconds\":0,\"streams\":["
            + "{\"id\":0,\"kind\":\"continuous\",\"name\":\"continuous\",\"firstTick\":0,"
            + "\"periodTicks\":512,\"interpolation\":\"linear\",\"encoding\":\"frame-f32-v1\","
            + "\"components\":[]},"
            + "{\"id\":1,\"kind\":\"discrete\",\"name\":\"peaks\",\"encoding\":\"peak-v1\","
            + "\"eventType\":\"peak\",\"channels\":[]}]}";

        public static void Write(string path)
        {
            byte[] descriptor = Encoding.UTF8.GetBytes(Descriptor);
            var entries = new List<ChunkEntry>();
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(new byte[64]);
            writer.Write(descriptor);
            WritePadding(writer, descriptor.Length);
            WriteContinuous(writer, entries);
            WriteEvents(writer, entries);

            long indexOffset = stream.Position;
            using var entryStream = new MemoryStream();
            using var entryWriter = new BinaryWriter(entryStream, Encoding.UTF8, true);
            foreach (ChunkEntry entry in entries)
            {
                entryWriter.Write(entry.Kind);
                entryWriter.Write(entry.Codec);
                entryWriter.Write((ushort)0);
                entryWriter.Write(entry.StreamId);
                entryWriter.Write(entry.Offset);
                entryWriter.Write(entry.StoredSize);
                entryWriter.Write(entry.RecordCount);
                entryWriter.Write(entry.FirstTick);
                entryWriter.Write(entry.LastTick);
                entryWriter.Write(entry.PayloadSize);
                entryWriter.Write(0u);
            }
            entryWriter.Flush();
            byte[] rawIndex = entryStream.ToArray();
            writer.Write(Encoding.ASCII.GetBytes("XASI"));
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((ushort)48);
            writer.Write((ushort)0);
            writer.Write((uint)entries.Count);
            writer.Write((ulong)(indexOffset + 32));
            writer.Write(Crc32C(rawIndex, rawIndex.Length));
            writer.Write(0u);
            writer.Write(rawIndex);
            ulong fileSize = (ulong)stream.Position;

            byte[] prefix;
            using (var headerStream = new MemoryStream())
            using (var headerWriter = new BinaryWriter(headerStream, Encoding.UTF8, true))
            {
                headerWriter.Write(Encoding.ASCII.GetBytes("XAS1"));
                headerWriter.Write((ushort)1);
                headerWriter.Write((ushort)0);
                headerWriter.Write((ushort)64);
                headerWriter.Write((ushort)3);
                headerWriter.Write(SampleRate);
                headerWriter.Write((ulong)512);
                headerWriter.Write(64u);
                headerWriter.Write((uint)descriptor.Length);
                headerWriter.Write((ulong)indexOffset);
                headerWriter.Write((uint)entries.Count);
                headerWriter.Write(0u);
                headerWriter.Write(fileSize);
                headerWriter.Write(Crc32C(descriptor, descriptor.Length));
                headerWriter.Flush();
                prefix = headerStream.ToArray();
            }

            byte[] header = new byte[64];
            Buffer.BlockCopy(prefix, 0, header, 0, 60);
            Buffer.BlockCopy(BitConverter.GetBytes(Crc32C(header, 60)), 0, header, 60, 4);
            stream.Position = 0;
            writer.Write(header);
        }

        private static void WriteContinuous(
            BinaryWriter writer,
            List<ChunkEntry> entries)
        {
            using var payloadStream = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true);
            WriteFrame(payloadWriter, -20f);
            WriteFrame(payloadWriter, -10f);
            payloadWriter.Flush();
            WriteChunk(writer, entries, 1, 1, 0, payloadStream.ToArray(), 2, 0, 512);
        }

        private static void WriteFrame(BinaryWriter writer, float rms)
        {
            writer.Write(rms);
            writer.Write(-30f);
            writer.Write(-40f);
            writer.Write(-50f);
            writer.Write(1000f);
        }

        private static void WriteEvents(
            BinaryWriter writer,
            List<ChunkEntry> entries)
        {
            using var payloadStream = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, true);
            payloadWriter.Write((byte)0);
            payloadWriter.Write((byte)0);
            payloadWriter.Write((ushort)0);
            payloadWriter.Write(256u);
            payloadWriter.Write(256u);
            payloadWriter.Write(-12f);
            payloadWriter.Write(0.8f);
            payloadWriter.Flush();
            WriteChunk(writer, entries, 2, 2, 1, payloadStream.ToArray(), 1, 0, 512);
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
            long offset = writer.BaseStream.Position;
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
                (ulong)offset,
                (uint)(writer.BaseStream.Position - offset),
                recordCount,
                firstTick,
                lastTick,
                (uint)payload.Length));
        }

        private static void WritePadding(BinaryWriter writer, int length)
        {
            int count = (8 - (length & 7)) & 7;
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

        private static uint Crc32C(byte[] bytes, int length)
        {
            uint crc = uint.MaxValue;
            for (int index = 0; index < length; index++)
                crc = CrcTable[(crc ^ bytes[index]) & 0xFF] ^ (crc >> 8);
            return crc ^ uint.MaxValue;
        }

        private readonly struct ChunkEntry
        {
            public readonly byte Kind;
            public readonly byte Codec;
            public readonly uint StreamId;
            public readonly ulong Offset;
            public readonly uint StoredSize;
            public readonly uint RecordCount;
            public readonly ulong FirstTick;
            public readonly ulong LastTick;
            public readonly uint PayloadSize;

            public ChunkEntry(
                byte kind,
                byte codec,
                uint streamId,
                ulong offset,
                uint storedSize,
                uint recordCount,
                ulong firstTick,
                ulong lastTick,
                uint payloadSize)
            {
                Kind = kind;
                Codec = codec;
                StreamId = streamId;
                Offset = offset;
                StoredSize = storedSize;
                RecordCount = recordCount;
                FirstTick = firstTick;
                LastTick = lastTick;
                PayloadSize = payloadSize;
            }
        }
    }
}
