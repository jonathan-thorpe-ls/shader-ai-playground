using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public readonly struct AttributeDependency
    {
        public AttributeDependency(
            AttributeId id,
            AttributeDomain domain,
            AttributeFormat format)
        {
            Id = id;
            Domain = domain;
            Format = format;
        }

        public AttributeId Id { get; }
        public AttributeDomain Domain { get; }
        public AttributeFormat Format { get; }
    }

    public interface IAttributeOperator
    {
        string Name { get; }
        IReadOnlyList<AttributeDependency> Inputs { get; }
        IReadOnlyList<AttributeDescriptor> Outputs { get; }
        void Validate(AttributeSet attributes);
        void Record(CommandBuffer command, GeometryResource resource);
    }

    public sealed class AttributeRecipe
    {
        private readonly List<IAttributeOperator> _operations = new();

        public AttributeRecipe(string name)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Recipe name cannot be empty.", nameof(name))
                : name;
        }

        public string Name { get; }
        public IReadOnlyList<IAttributeOperator> Operations => _operations;

        public void Add(IAttributeOperator operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            _operations.Add(operation);
        }

        public void Execute(GeometryResource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            Dictionary<AttributeId, AttributeDescriptor> produced = new();
            foreach (IAttributeOperator operation in _operations)
            {
                ValidateOperation(operation, resource.Attributes, produced);
                foreach (AttributeDescriptor output in operation.Outputs)
                {
                    resource.Attributes.RequireOrCreate(output);
                    produced[output.Id] = output;
                }
            }

            using GeometryCommandScheduler scheduler =
                new($"{resource.Layout.Name} Recipe {Name}");
            scheduler.Record(command =>
            {
                foreach (IAttributeOperator operation in _operations)
                {
                    command.BeginSample($"ProcGeo.Attributes.{operation.Name}");
                    operation.Record(command, resource);
                    command.EndSample($"ProcGeo.Attributes.{operation.Name}");
                }
            });
            scheduler.ExecuteOnce();
        }

        private static void ValidateOperation(
            IAttributeOperator operation,
            AttributeSet attributes,
            IReadOnlyDictionary<AttributeId, AttributeDescriptor> produced)
        {
            foreach (AttributeDependency dependency in operation.Inputs)
            {
                AttributeDescriptor descriptor;
                if (attributes.TryGet(dependency.Id, out AttributeSet.Entry entry))
                    descriptor = entry.Descriptor;
                else if (!produced.TryGetValue(dependency.Id, out descriptor))
                    throw new InvalidOperationException(
                        $"Operation '{operation.Name}' requires missing attribute " +
                        $"'{dependency.Id}'.");

                if (descriptor.Domain != dependency.Domain ||
                    descriptor.Format != dependency.Format)
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation.Name}' requires '{dependency.Id}' as " +
                        $"{dependency.Domain}/{dependency.Format}, but it is " +
                        $"{descriptor.Domain}/{descriptor.Format}.");
                }
            }
            operation.Validate(attributes);
        }
    }

    public sealed class AttributeStats
    {
        public float Minimum { get; set; }
        public float Maximum { get; set; }
        public float Mean { get; set; }
        public uint FiniteCount { get; set; }
        public uint InvalidCount { get; set; }
    }
}
