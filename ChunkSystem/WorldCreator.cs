using System;
using Cysharp.Threading.Tasks;
using EcsEvents;
using Fusion;
using UnityEngine;
using Voody.UniLeo.Lite;
using Zenject;

namespace RPGGame.Gameplay.Shared
{
    public sealed class WorldCreator : NetworkBehaviour
    {
        [SerializeField] private EntityObject _entityObject;

        [Inject]
        private readonly IEcsEventBus _eventBus;

        public event Action OnWorldCreateStarted;
        public event Action OnWorldCreateFinished;

        public override void Spawned()
        {
            if (Runner.IsServer)
            {
                InitializeAsync().Forget();
            }
        }

        private async UniTaskVoid InitializeAsync()
        {
            RPC_WorldCreateStarted();

            await UniTask.Delay(TimeSpan.FromSeconds(1), ignoreTimeScale: true);

            await NetworkManager.Instance.AdditiveSceneLoader.LoadSceneTask(SceneLoadData.Instance.SceneName);

            _eventBus.RaiseEvent(new OnChunksCreate()
            {
                SourceEntity = _entityObject.PackedEntity,
                SceneConfig = NetworkManager.Instance.SceneConfig
            });
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_WorldCreateStarted()
        {
            OnWorldCreateStarted?.Invoke();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_WorldCreateFinished()
        {
            OnWorldCreateFinished?.Invoke();
        }
    }
}
