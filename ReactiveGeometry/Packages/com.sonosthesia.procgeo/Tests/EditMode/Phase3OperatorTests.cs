using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase3OperatorTests
    {
        [Test]
        public void GraphDistance_MatchesCpuBreadthFirstSearch()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            using GraphDistanceOperator operation = new(new[] { 0 });
            operation.Execute(context.Resource);

            float[] actual = Read<float>(
                context.Resource.Attributes.Require(AttributeIds.GraphDistance).Buffer);
            int[] expected = CpuDistances(context.Resource, 0);
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int index = 0; index < actual.Length; index++)
                Assert.That(actual[index], Is.EqualTo(expected[index]));
        }

        [Test]
        public void GraphDistance_MultipleSourcesUseClosestRing()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            uint[] neighbours = Read<uint>(context.Resource.Topology.Neighbours);
            int second = (int)neighbours[0];
            using GraphDistanceOperator operation = new(new[] { 0, second });
            operation.Execute(context.Resource);

            float[] actual = Read<float>(
                context.Resource.Attributes.Require(AttributeIds.GraphDistance).Buffer);
            int[] fromZero = CpuDistances(context.Resource, 0);
            int[] fromSecond = CpuDistances(context.Resource, second);
            for (int index = 0; index < actual.Length; index++)
            {
                int expected = Mathf.Min(fromZero[index], fromSecond[index]);
                Assert.That(actual[index], Is.EqualTo(expected));
            }
        }

        [Test]
        public void AttributeRecipe_ReusesCompatibleDynamicOutput()
        {
            using GeometryTestContext context = GeometryTestContext.Build(1);
            AttributeId output = new("Phase3TestFill");
            AttributeRecipe recipe = new("Fill");
            recipe.Add(ScalarAttributeOperator.Fill(output, 0.75f));

            recipe.Execute(context.Resource);
            GraphicsBuffer buffer =
                context.Resource.Attributes.Require(output).Buffer;
            recipe.Execute(context.Resource);

            Assert.That(
                context.Resource.Attributes.Require(output).Buffer,
                Is.SameAs(buffer));
            foreach (float value in Read<float>(buffer))
                Assert.That(value, Is.EqualTo(0.75f));
        }

        [Test]
        public void AttributeRecipe_RejectsMissingInputs()
        {
            using GeometryTestContext context = GeometryTestContext.Build(1);
            AttributeRecipe recipe = new("Missing");
            recipe.Add(ScalarAttributeOperator.Remap(
                new AttributeId("NotPresent"),
                new AttributeId("Remapped"),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f)));

            Assert.Throws<InvalidOperationException>(() =>
                recipe.Execute(context.Resource));
        }

        private static int[] CpuDistances(GeometryResource resource, int source)
        {
            int vertexCount = resource.Layout.VertexCount;
            uint[] offsets = Read<uint>(resource.Topology.NeighbourOffsets);
            uint[] neighbours = Read<uint>(resource.Topology.Neighbours);
            int[] distances = new int[vertexCount];
            for (int index = 0; index < distances.Length; index++)
                distances[index] = -1;
            Queue<int> queue = new();
            distances[source] = 0;
            queue.Enqueue(source);
            while (queue.Count > 0)
            {
                int vertex = queue.Dequeue();
                for (uint slot = offsets[vertex];
                     slot < offsets[vertex + 1];
                     slot++)
                {
                    int neighbour = (int)neighbours[slot];
                    if (distances[neighbour] >= 0)
                        continue;
                    distances[neighbour] = distances[vertex] + 1;
                    queue.Enqueue(neighbour);
                }
            }
            return distances;
        }

        private static T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] result = new T[buffer.count];
            buffer.GetData(result);
            return result;
        }
    }
}
