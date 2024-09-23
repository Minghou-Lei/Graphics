using UnityEngine;
using static UnityEditor.LightBaking.InputExtraction;

namespace UnityEditor.LightBaking.Tests
{
    internal static class SourceMapUtil
    {
        public static uint? LookupInstanceIndex(SourceMap map, string gameObjectName)
        {
            GameObject go = GameObject.Find(gameObjectName);
            if (go == null)
                return new uint?();

            int instanceID = go.GetInstanceID();
            int instanceIndex = map.GetInstanceIndex(instanceID);

            if (instanceIndex == -1)
                return new uint?();

            return (uint)instanceIndex;
        }
    }
}
