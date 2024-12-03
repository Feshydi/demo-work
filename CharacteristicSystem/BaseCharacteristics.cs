using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CustomDynamicEnums;
using Fusion;

namespace RPGGame.Gameplay.Shared
{
    [Serializable]
    public struct BaseCharacteristics : INetworkStruct
    {
        [UnitySerializeField]
        [Networked, Capacity(Constants.Player.Stats.CharacteristicsCount)]
        private NetworkDictionary<CharacteristicType, BaseAttributeData> Data => default;

        public BaseAttributeData this[CharacteristicType characteristic]
        {
            get => Data.Get(characteristic);
            set => Data.Set(characteristic, value);
        }

        public IEnumerable<CharacteristicType> GetExistingTypes() => Data.Select(characteristic => characteristic.Key);

        public bool Contains(CharacteristicType skill) => Data.ContainsKey(skill);

        public string ToString(NetworkRunner runner)
        {
            StringBuilder stringBuilder = new();
            foreach (var characteristic in Data)
                stringBuilder.Append($"{characteristic.Key.ToString()}:{characteristic.Value.Value(runner)}; ");
            return stringBuilder.ToString();
        }
    }
}
