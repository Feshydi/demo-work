using CustomDynamicEnums;
using EcsEvents;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using RPGGame.Gameplay.Shared;
using UnityEngine;
using Zenject;

namespace RPGGame.Gameplay.Server
{
    public struct OnChangeCharacteristicRate : IEcsEvent
    {
        public EcsPackedEntity TargetEntity;
        public CharacteristicType Characteristic;
        public float ChangeRate;
    }

    public struct OnChangeCharacteristicValue : IEcsEvent
    {
        public EcsPackedEntity TargetEntity;
        public CharacteristicType Type;
        public float ChangeValue;
    }

    public sealed class CharacteristicSystem : IEcsRunSystem
    {
        [Inject]
        private readonly IEcsEventBus _eventBus;

        private readonly EcsSharedInject<EcsSharedData> _sharedData = default;
        private readonly EcsWorldInject _world = default;

        private readonly EcsPoolInject<CharacteristicsData> _characteristicsPool = default;

        public void Run(IEcsSystems systems)
        {
            var runner = _sharedData.Value.Runner;

            foreach (var onChangeRate in _eventBus.GetEvents<OnChangeCharacteristicRate>())
            {
                if (!onChangeRate.TargetEntity.Unpack(_world.Value, out var targetEntity))
                    continue;

                var type = onChangeRate.Characteristic;
                var rate = onChangeRate.ChangeRate;

                ref var characteristicsData = ref _characteristicsPool.Value.Get(targetEntity);
                var attribute = characteristicsData.GetAttribute(type);
                if (HasAttribute(attribute) == false)
                    continue;

                var characteristic = attribute.Value;
                var newChangeRate = attribute.Value.ChangeRate + rate;

                characteristic.UpdateChangeRate(runner, newChangeRate);
                characteristicsData.SetAttribute(type, characteristic);
            }

            foreach (var onChange in _eventBus.GetEvents<OnChangeCharacteristicValue>())
            {
                if (!onChange.TargetEntity.Unpack(_world.Value, out var targetEntity))
                    continue;

                var type = onChange.Type;

                ref var characteristicsData = ref _characteristicsPool.Value.Get(targetEntity);
                var attribute = characteristicsData.GetAttribute(type);
                if (HasAttribute(attribute) == false)
                    continue;

                var characteristic = attribute.Value;
                var value = characteristic.RawValue(runner) + onChange.ChangeValue;
                value = Mathf.Clamp(value, characteristic.MinValue, characteristic.MaxValue);

                characteristic.SetTracker(runner, value, characteristic.ChangeRate);
                characteristicsData.SetAttribute(type, characteristic);

                _eventBus.RaiseEvent(new OnCharacteristicValueChanged1()
                {
                    TargetEntity = onChange.TargetEntity,
                    Type = type
                });
            }
        }

        private bool HasAttribute(BaseAttributeData attribute)
        {
            if (attribute == null)
            {
                Debug.LogError("Whoops, it looks like you didn't properly check " +
                               $"if {targetEntity} has the {type} characteristic!");
                return false;
            }
            return true;
        }
    }
}
