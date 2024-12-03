using CustomDynamicEnums;
using EcsEvents;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using RPGGame.Gameplay.Shared;
using UnityEngine;
using Zenject;

namespace RPGGame.Gameplay.Server
{
    public sealed class DamageSystem : IEcsRunSystem
    {
        [Inject]
        private readonly IEcsEventBus _eventBus;

        private readonly EcsWorldInject _world = default;
        private readonly EcsPoolInject<CharacteristicsData> _characteristicsPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var onDamage in _eventBus.GetEvents<OnDamage>())
            {
                if (!onDamage.TargetEntity.Unpack(_world.Value, out var targetEntity))
                    continue;

                if (_characteristicsPool.Value.Has(targetEntity) == false)
                    continue;

                var damage = Mathf.Abs(onDamage.Damage);
                _eventBus.RaiseEvent(new OnChangeCharacteristicValue()
                {
                    TargetEntity = _world.Value.PackEntity(targetEntity),
                    Type = CharacteristicType.Health,
                    ChangeValue = -damage
                });

                var message = $"Damage {damage}";
                MessengerUtility.TrySendMessage(_world.Value, targetEntity, message, MessageType.Damage);
            }
        }
    }
}
