using Fusion;
using UnityEngine;
using Voody.UniLeo.Lite;

namespace RPGGame.Gameplay.Shared
{
    public sealed class EntityCharacteristics : NetworkBehaviour
    {
        [SerializeField] private EntityObject _entityObject;
        [SerializeField] private GameplayEvents _gameplayEvents;
        
        private ChangeDetector _changeDetector;
        
        [Networked]
        public BaseCharacteristics BaseCharacteristics { get; set; }

        public override void Spawned()
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        }

        public override void Render()
        {
            foreach (var change in _changeDetector.DetectChanges(this, out _, out _))
            {
                switch (change)
                {
                    case nameof(BaseCharacteristics):
                        OnCharacteristicsChanged();
                        break;
                }
            }
        }

        private void OnCharacteristicsChanged()
        {
            _gameplayEvents.OnCharacteristicsChanged?.Invoke(_entityObject.Entity, BaseCharacteristics);
        }
    }
}
