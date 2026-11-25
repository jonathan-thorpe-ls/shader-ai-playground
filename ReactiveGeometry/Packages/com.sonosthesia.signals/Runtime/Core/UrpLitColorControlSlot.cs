using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.Signals
{
    [DisallowMultipleComponent]
    public sealed class UrpLitColorControlSlot :
        MonoBehaviour,
        IControlSlotTarget,
        IControlColorSlotTarget
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly ControlSignalId ColorSlotId =
            ControlSignalId.FromString("material.color");

        [SerializeField] private Renderer targetRenderer;
        [SerializeField, Min(0)] private int materialIndex;
        [SerializeField] private bool createMaterialInstance;

        private readonly List<Material> _materials = new(4);
        private MaterialPropertyBlock _propertyBlock;
        private bool _baseColorSupported;

        public int ControlSlotCount => 1;
        public Color LastColor { get; private set; }

        public void Configure(Renderer renderer, int index = 0, bool instance = false)
        {
            targetRenderer = renderer;
            materialIndex = Mathf.Max(index, 0);
            createMaterialInstance = instance;
            Rebuild();
        }

        public bool TryGetControlSlot(ControlSignalId id, out int slotHandle)
        {
            slotHandle = id == ColorSlotId ? 0 : -1;
            return slotHandle == 0;
        }

        public ControlSignalType GetControlSlotType(int slotHandle)
        {
            return slotHandle == 0
                ? ControlSignalType.Color
                : throw new ArgumentOutOfRangeException(nameof(slotHandle));
        }

        public void WriteFloat(int slotHandle, float value)
        {
            throw new InvalidOperationException("The URP Lit adapter accepts Color only.");
        }

        public void WritePulse(int slotHandle, in ControlPulse pulse)
        {
            throw new InvalidOperationException("The URP Lit adapter accepts Color only.");
        }

        public void WriteColor(int slotHandle, Color value)
        {
            if (slotHandle != 0)
                throw new ArgumentOutOfRangeException(nameof(slotHandle));
            if (!_baseColorSupported || targetRenderer == null)
                return;

            LastColor = value;
            if (createMaterialInstance)
            {
                targetRenderer.GetMaterials(_materials);
                if ((uint)materialIndex < (uint)_materials.Count)
                    _materials[materialIndex].SetColor(BaseColorId, value);
                return;
            }

            targetRenderer.GetPropertyBlock(_propertyBlock, materialIndex);
            _propertyBlock.SetColor(BaseColorId, value);
            targetRenderer.SetPropertyBlock(_propertyBlock, materialIndex);
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Rebuild();
        }

        private void Rebuild()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            _baseColorSupported = false;
            if (targetRenderer == null)
                return;
            if (createMaterialInstance)
                targetRenderer.GetMaterials(_materials);
            else
                targetRenderer.GetSharedMaterials(_materials);
            if ((uint)materialIndex >= (uint)_materials.Count || _materials[materialIndex] == null)
                return;

            Material material = _materials[materialIndex];
            _baseColorSupported = material.HasProperty(BaseColorId);
            if (!_baseColorSupported)
                Debug.LogWarning(
                    $"Material '{material.name}' has no _BaseColor property.",
                    this);
        }
    }
}
