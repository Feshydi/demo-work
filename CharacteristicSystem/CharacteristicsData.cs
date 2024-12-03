using System.Collections.Generic;
using CustomDynamicEnums;
using Leopotam.EcsLite;
using RPGGame.Gameplay.Shared;

namespace RPGGame.Gameplay.Server
{
    public struct CharacteristicsData : IEcsAutoCopy<CharacteristicsData>
    {
        /// <summary>
        /// Instead, use methods to manage characteristics
        /// </summary>
        public EntityCharacteristics EntityCharacteristics;

        private BaseCharacteristics _characteristics;

        /// <summary>
        /// Gets the value of the specified attribute.
        /// </summary>
        /// <returns>The value of the attribute if it exists, otherwise null.</returns>
        public BaseAttributeData? GetAttribute(CharacteristicType characteristic)
        {
            return _characteristics.Contains(characteristic) ? _characteristics[characteristic] : null;
        }

        /// <summary>
        /// Sets the value of the specified attribute. Will add a new attribute if attribute does not already exist.
        /// </summary>
        public void SetAttribute(CharacteristicType characteristic, BaseAttributeData value)
        {
            _characteristics[characteristic] = value;
            UpdateEntity();
        }

        /// <summary>
        /// Retrieves the current set of all characteristic types.
        /// </summary>
        public IEnumerable<CharacteristicType> GetExistingTypes() => _characteristics.GetExistingTypes();

        /// <summary>
        /// Sets all characteristics from the given BaseCharacteristics struct.
        /// </summary>
        public void SetCharacteristics(BaseCharacteristics characteristics)
        {
            _characteristics = characteristics;
            UpdateEntity();
        }

        private void UpdateEntity()
        {
            if (EntityCharacteristics == null)
                return;
            EntityCharacteristics.BaseCharacteristics = _characteristics;
        }

        public void AutoCopy(ref CharacteristicsData src, ref CharacteristicsData dst)
        {
            dst.EntityCharacteristics = src.EntityCharacteristics;
            dst.SetCharacteristics(src._characteristics);
        }

        public void CopyValues(ref CharacteristicsData destination)
        {
            destination._characteristics = _characteristics;
        }
    }
}
