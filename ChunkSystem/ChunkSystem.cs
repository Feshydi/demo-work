using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using EcsEvents;
using Fusion;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using RPGGame.Gameplay.Shared;
using UnityEngine;
using Voody.UniLeo.Lite;
using Zenject;

namespace RPGGame.Gameplay.Server
{
    public sealed class ChunkSystem : IEcsRunSystem
    {
        [Inject]
        private readonly IEcsEventBus _eventBus;

        private readonly EcsSharedInject<EcsSharedData> _sharedData = default;
        private readonly EcsWorldInject _world = default;

        private readonly EcsFilterInject<Inc<PlayerBehaviourData>> _playerFilter = default;

        private readonly EcsPoolInject<ChunkData> _chunkPool = default;
        private readonly EcsPoolInject<TransformData> _transformPool = default;
        private readonly EcsPoolInject<SavedTransformData> _savedTransformPool = default;
        private readonly EcsPoolInject<WorldCreatorData> _worldCreatorPool = default;

        private readonly Dictionary<Vector2Int, ChunkInfo> _chunks = new();
        private readonly Dictionary<Vector2Int, HashSet<int>> _loadedChunks = new();

        private readonly Dictionary<int, Vector2Int> _playersChunk = new();

        private readonly Dictionary<GameObject, int> _objectEntityTemplates = new();

        private readonly List<Vector2Int> _loadQueue = new();
        private const int ChunkLoadInterval = 4;
        private int MaxLoadQueueSize => _sharedData.Value.Runner.TickRate / ChunkLoadInterval;
        private TickTimer _chunkLoadTimer;

        private ChunkCreatorData? _creatorData;
        private ChunkCreatorData CreatorData => _creatorData!.Value;
        private float _offsetX;
        private float _offsetZ;

        public void Run(IEcsSystems systems)
        {
            var runner = _sharedData.Value.Runner;

            foreach (var playerEntity in _playerFilter.Value)
            {
                if (_creatorData == null)
                    break;

                var playerPosition = _transformPool.Value.Get(playerEntity).Transform.position;
                var chunk = ChunkByWorldPosition(playerPosition);

                if (_playersChunk.TryGetValue(playerEntity, out var currentChunk) &&
                    currentChunk == chunk)
                    continue;

                _playersChunk[playerEntity] = chunk;

                if (_chunks.ContainsKey(chunk) == false)
                {
                    Debug.LogError($"Shouldn't happen, but chunk {chunk} does not exist");
                    continue;
                }

                HashSet<Vector2Int> chunksToLoad = new();
                foreach (var offset in CreatorData.ChunkLoadMask)
                {
                    var targetChunk = chunk + offset;
                    if (_chunks.ContainsKey(targetChunk) == false)
                        continue;

                    chunksToLoad.Add(targetChunk);
                }

                HashSet<Vector2Int> chunksToUnload = new();
                foreach (var (loadedChunk, players) in _loadedChunks)
                {
                    if (players.Contains(playerEntity) == false)
                        continue;

                    chunksToUnload.Add(loadedChunk);
                }

                HashSet<Vector2Int> commonElements = new(chunksToUnload);
                commonElements.RemoveWhere(item => chunksToLoad.Contains(item) == false);
                chunksToUnload.RemoveWhere(item => commonElements.Contains(item));
                chunksToLoad.RemoveWhere(item => commonElements.Contains(item));

                foreach (var chunkToLoad in chunksToLoad)
                {
                    if (_loadedChunks.TryAdd(chunkToLoad, new HashSet<int>() { playerEntity }) == false)
                    {
                        _loadedChunks[chunkToLoad].Add(playerEntity);
                    }

                    if (_loadedChunks[chunkToLoad].Count == 1)
                    {
                        _loadQueue.Add(chunkToLoad);
                    }
                }

                foreach (var chunkToUnload in chunksToUnload.ToList())
                {
                    _loadedChunks[chunkToUnload].Remove(playerEntity);

                    if (_loadedChunks[chunkToUnload].Count > 0)
                    {
                        chunksToUnload.Remove(chunkToUnload);
                        continue;
                    }

                    if (_loadQueue.Contains(chunkToUnload))
                    {
                        _loadQueue.Remove(chunkToUnload);
                        chunksToUnload.Remove(chunkToUnload);
                    }

                    _loadedChunks.Remove(chunkToUnload);
                }

                var despawnCount = 0;
                foreach (var chunkToUnloadEntity in chunksToUnload.Select(chunkToUnload => _chunks[chunkToUnload].Entity))
                {
                    ref var chunkData = ref _chunkPool.Value.Get(chunkToUnloadEntity);
                    foreach (var packedEntities in chunkData.Entities.Values)
                    {
                        foreach (var packedEntity in packedEntities)
                        {
                            if (!packedEntity.Unpack(_world.Value, out var objectEntity))
                                continue;

                            ref var transformData = ref _transformPool.Value.Get(objectEntity);
                            var networkObject = transformData.Transform.GetComponent<NetworkObject>();
                            runner.Despawn(networkObject);

                            SetDataLinksNull(objectEntity);

                            despawnCount++;
                        }
                    }
                }
                Debug.Log($"Despawned {despawnCount} objects");
            }

            if (_loadQueue.Count > 0 && _chunkLoadTimer.ExpiredOrNotRunning(runner))
            {
                _chunkLoadTimer = TickTimer.CreateFromTicks(runner, ChunkLoadInterval);

                do
                {
                    const int index = 0;
                    var chunkToLoad = _loadQueue[index];
                    var chunkToLoadEntity = _chunks[chunkToLoad].Entity;
                    _loadQueue.RemoveAt(index);

                    var spawnCount = 0;

                    ref var chunkData = ref _chunkPool.Value.Get(chunkToLoadEntity);

                    Dictionary<int, NetworkSpawnOp> spawnTasks = new();
                    foreach (var (prefab, packedEntities) in chunkData.Entities)
                    {
                        foreach (var packedEntity in packedEntities)
                        {
                            if (!packedEntity.Unpack(_world.Value, out var objectEntity))
                                continue;

                            spawnCount++;
                            ref var savedTransformData = ref _savedTransformPool.Value.Get(objectEntity);
                            var position = savedTransformData.Position;
                            var rotation = savedTransformData.Rotation;

                            var op = runner.SpawnAsync(prefab, position, rotation);
                            spawnTasks[objectEntity] = op;
                        }
                    }

                    SpawnObjects(spawnTasks);

                    Debug.Log($"Spawned {spawnCount} objects in Chunk{chunkToLoad}");
                } while (_loadQueue.Count >= MaxLoadQueueSize);
            }

            foreach (var onChunksCreate in _eventBus.GetEvents<OnChunksCreate>())
            {
                if (!onChunksCreate.SourceEntity.Unpack(_world.Value, out var sourceEntity) ||
                    !onChunksCreate.SceneConfig)
                    continue;

                var sceneConfig = onChunksCreate.SceneConfig;
                _creatorData = new ChunkCreatorData()
                {
                    XSize = sceneConfig.MapSizeSettings.XBounds,
                    ZSize = sceneConfig.MapSizeSettings.ZBounds,
                    ChunkSize = sceneConfig.ChunkSettings.ChunkSize,
                    ChunkLoadMask = sceneConfig.ChunkSettings.ChunkLoadMask
                };

                var chunkCreatorData = _creatorData!.Value;

                var worldSize = new Vector2(
                    chunkCreatorData.XSize.y - chunkCreatorData.XSize.x,
                    chunkCreatorData.ZSize.y - chunkCreatorData.ZSize.x);
                var chunkSize = chunkCreatorData.ChunkSize;

                var chunksX = SideChunksCount(worldSize.x, chunkSize, out _offsetX);
                var chunksZ = SideChunksCount(worldSize.y, chunkSize, out _offsetZ);

                for (var x = 0; x < chunksX; x++)
                {
                    for (var z = 0; z < chunksZ; z++)
                    {
                        var chunkEntity = _world.Value.NewEntity();
                        ref var chunkData = ref _chunkPool.Value.Add(chunkEntity);
                        chunkData.Entities = new Dictionary<NetworkObject, List<EcsPackedEntity>>();

                        var chunk = new Vector2Int(x, z);
                        _chunks[chunk] = new ChunkInfo() { Entity = chunkEntity };
                    }
                }

                var initCount = 0;

                var outsideMapPosition = new Vector3(chunkCreatorData.XSize.y + 100f, 0f, 0f);
                foreach (var spawnData in sceneConfig.SpawnObjects.Data)
                {
                    if (spawnData.Prefab == false)
                    {
                        Debug.LogError("Skipping prefab due to missing prefab.");
                        continue;
                    }

                    var templateNetworkObject =
                        runner.Spawn(spawnData.Prefab, outsideMapPosition, Quaternion.identity);
                    var templateEntity = templateNetworkObject.GetComponent<EntityObject>().Entity;

                    SetDataLinksNull(templateEntity);

                    foreach (var transform in spawnData.Transforms)
                    {
                        var chunk = ChunkByWorldPosition(transform.Position);
                        if (_chunks.TryGetValue(chunk, out var chunkPosition) == false)
                            continue;

                        var newEntity = _world.Value.NewEntity();

                        ref var savedTransformData = ref _savedTransformPool.Value.Add(newEntity);
                        savedTransformData.Position = transform.Position;
                        savedTransformData.Rotation = transform.Rotation;

                        _world.Value.CopyEntity(templateEntity, newEntity);

                        ref var chunkData = ref _chunkPool.Value.Get(chunkPosition.Entity);
                        var packedEntity = _world.Value.PackEntity(newEntity);

                        if (chunkData.Entities.ContainsKey(spawnData.Prefab))
                        {
                            chunkData.Entities[spawnData.Prefab].Add(packedEntity);
                        }
                        else
                        {
                            chunkData.Entities[spawnData.Prefab] = new List<EcsPackedEntity>()
                            {
                                packedEntity
                            };
                        }

                        initCount++;
                    }

                    Object.Destroy(templateNetworkObject.gameObject);
                    _world.Value.DelEntity(templateEntity);
                }

                _worldCreatorPool.Value.Get(sourceEntity).WorldCreator.RPC_WorldCreateFinished();
                Debug.Log($"Initialized {initCount} objects in {_chunks.Count} chunks");
            }
        }

        private static int SideChunksCount(float sideLength, float chunkSize, out float offset)
        {
            var count = Mathf.CeilToInt(sideLength / chunkSize);
            var totalChunkWidth = count * chunkSize;
            offset = (totalChunkWidth - sideLength) / 2f;
            return count;
        }

        private Vector2Int ChunkByWorldPosition(Vector3 position)
        {
            var positionX = position.x - CreatorData.XSize.x + _offsetX;
            var positionZ = position.z - CreatorData.ZSize.x + _offsetZ;

            var chunkX = Mathf.RoundToInt(positionX / CreatorData.ChunkSize);
            var chunkZ = Mathf.RoundToInt(positionZ / CreatorData.ChunkSize);

            return new Vector2Int(chunkX, chunkZ);
        }

        private void SetDataLinksNull(int objectEntity)
        {
            var characteristicsPool = _world.Value.GetPool<CharacteristicsData>();
            if (characteristicsPool.Has(objectEntity))
            {
                characteristicsPool.Get(objectEntity).EntityCharacteristics = null;
            }

            // Set another components links to null
        }

        private void SpawnObjects(Dictionary<int, NetworkSpawnOp> ops) => SpawnObjectsAsync(ops).Forget();

        private async UniTaskVoid SpawnObjectsAsync(Dictionary<int, NetworkSpawnOp> ops)
        {
            foreach (var (objectEntity, op) in ops)
            {
                await op;

                var loadedEntityObject = op.Object.GetComponent<EntityObject>();

                if (_objectEntityTemplates.TryGetValue(loadedEntityObject.gameObject, out var templateEntity) == false)
                {
                    templateEntity = loadedEntityObject.Entity;
                    _objectEntityTemplates[loadedEntityObject.gameObject] = templateEntity;
                }

                CopyEntityData(_world.Value, objectEntity, templateEntity);
                _world.Value.CopyEntity(templateEntity, objectEntity);
                loadedEntityObject.Initialize(_world.Value.PackEntity(objectEntity), _world.Value);
            }
        }

        private static void CopyEntityData(EcsWorld world, int sourceEntity, int destinationEntity)
        {
            var characteristicsPool = world.GetPool<CharacteristicsData>();
            if (characteristicsPool.Has(sourceEntity))
            {
                if (characteristicsPool.Has(destinationEntity) == false)
                    characteristicsPool.Add(destinationEntity);

                ref var sourceCharacteristicsData = ref characteristicsPool.Get(sourceEntity);
                ref var destinationCharacteristicsData = ref characteristicsPool.Get(destinationEntity);
                sourceCharacteristicsData.CopyValues(ref destinationCharacteristicsData);
            }

            // Copy components data
        }

        private struct ChunkInfo
        {
            public int Entity;
        }

        public struct ChunkCreatorData
        {
            public Vector2 XSize;
            public Vector2 ZSize;
            public float ChunkSize;
            public Vector2Int[] ChunkLoadMask;
        }
    }
}
