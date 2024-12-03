using System.Collections.Generic;
using Fusion;
using Leopotam.EcsLite;

namespace RPGGame.Gameplay.Server
{
    public struct ChunkData
    {
        public Dictionary<NetworkObject, List<EcsPackedEntity>> Entities;
    }
}
