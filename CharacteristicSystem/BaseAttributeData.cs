using System;
using Fusion;
using UnityEngine;

namespace RPGGame.Gameplay.Shared
{
    [Serializable]
    public struct BaseAttributeData : INetworkStruct
    {
        public int MinValue;
        public int MaxValue;

        [Networked, UnitySerializeField]
        private FloatValueTracker ValueTracker { get; set; }

        public float RawValue(NetworkRunner runner) => ValueTracker.GetActualValue(runner, MinValue, MaxValue);

        public int Value(float rawValue) => Mathf.FloorToInt(rawValue);

        public int Value(NetworkRunner runner)
        {
            var rawValue = RawValue(runner);
            return Value(rawValue);
        }

        public float Exp(NetworkRunner runner)
        {
            var rawValue = RawValue(runner);
            return rawValue - Value(rawValue);
        }

        public float ChangeRate => ValueTracker.ChangeRate;

        public void SetTracker(NetworkRunner runner, float value, float changeRate)
        {
            ValueTracker = ValueTracker.NewTracker(runner, value, changeRate);
        }

        public void UpdateChangeRate(NetworkRunner runner, float changeRate)
        {
            ValueTracker = ValueTracker.UpdatedTracker(runner, changeRate, MinValue, MaxValue);
        }

        public float GetActualValue(NetworkRunner runner) =>
            ValueTracker.GetActualValue(runner, MinValue, MaxValue);
    }
}
