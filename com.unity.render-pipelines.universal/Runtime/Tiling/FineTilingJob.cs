using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompile(FloatPrecision.Medium, FloatMode.Fast)]
    unsafe struct FineTilingJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<LightMinMaxZ> minMaxZs;

        [ReadOnly]
        public NativeArray<TilingLightData> lights;

        public int lightsPerTile;

        [NativeDisableParallelForRestriction]
        public NativeArray<uint> tiles;

        public int2 screenResolution;

        public int2 groupResolution;

        public int2 tileResolution;

        public int tileWidth;

        public float3 viewForward;

        public float3 viewRight;

        public float3 viewUp;

        public float tileAperture;

        public const int groupWidth = 4;

        public const int groupLength = groupWidth * groupWidth;

        public void Execute(int groupIndex)
        {
            // Space suffixes:
            // - G group
            // - S screen space normalized
            // - P screen space pixels
            // - T tile
            // - W world
            var groupWidthP = groupWidth * tileWidth;
            var groupIdG = math.int2(groupIndex % groupResolution.x, groupIndex / groupResolution.x);
            var groupIdP = groupIdG * groupWidthP;
            var groupIdT = groupIdG * groupWidth;
            var groupRectS = new Rect((float2)groupIdP / screenResolution, math.float2(groupWidthP, groupWidthP) / screenResolution);

            var directionWs = stackalloc float3[groupLength];
            var apertures = stackalloc float[groupLength];
            var tilesOffsets = stackalloc int[groupLength];
            var currentLights = stackalloc uint[groupLength];

            for (var coneIndexG = 0; coneIndexG < groupLength; coneIndexG++)
            {
                var coneIdG = math.int2(coneIndexG % groupWidth, coneIndexG / groupWidth);
                var coneIdT = groupIdT + coneIdG;
                var coneCenterNDC = ((float)tileWidth * ((float2)coneIdT + 0.5f) / (float2)screenResolution) * 2.0f - 1.0f;
                var nearPlanePosition = viewForward + viewRight * coneCenterNDC.x + viewUp * coneCenterNDC.y;
                directionWs[coneIndexG] = nearPlanePosition / math.length(nearPlanePosition);
                apertures[coneIndexG] = tileAperture / math.length(nearPlanePosition);
                var coneIndexT = coneIdT.y * tileResolution.x + coneIdT.x;
                tilesOffsets[coneIndexG] = coneIndexT * (lightsPerTile / 32);
            }

            var lightCount = lights.Length;
            var wordCount = (lightCount + 31) / 32;
            for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
            {
                int end = math.min(wordIndex * 32 + 32, lightCount) % 33;
                for (var i = 0; i < end; i++)
                {
                    var lightIndex = wordIndex * 32 + i;
                    var light = lights[lightIndex];

                    if (!light.screenRect.Overlaps(groupRectS))
                    {
                        continue;
                    }

                    var lightMask = 1u << i;
                    if (light.lightType == LightType.Point)
                    {
                        ConeMarch(light.shape.sphere, ref light, lightIndex, lightMask, directionWs, apertures, currentLights);
                    }
                    else if (light.lightType == LightType.Spot)
                    {
                        ConeMarch(light.shape.cone, ref light, lightIndex, lightMask, directionWs, apertures, currentLights);
                    }
                }

                for (var coneIndexG = 0; coneIndexG < groupLength; coneIndexG++)
                {
                    var tileLightsIndex = tilesOffsets[coneIndexG] + wordIndex;
                    tiles[tileLightsIndex] = tiles[tileLightsIndex] | currentLights[coneIndexG];
                    currentLights[coneIndexG] = 0;
                }
            }
        }

        void ConeMarch<T>(
            T shape,
            ref TilingLightData light,
            int lightIndex,
            uint lightMask,
            [NoAlias] float3* directionWs,
            [NoAlias] float* apertures,
            [NoAlias] uint* currentLights
        ) where T : ICullingShape
        {
            var lightMinMax = minMaxZs[lightIndex];

            for (var coneIndexG = 0; coneIndexG < groupLength; coneIndexG++)
            {
                var directionW = directionWs[coneIndexG];
                var t = math.dot(lightMinMax.minZ * viewForward, directionW);
                var tMax = math.dot(lightMinMax.maxZ * viewForward, directionW);
                var hit = false;
                var aperture = apertures[coneIndexG];

                var directionL = math.mul(light.worldToLightMatrix, math.float4(directionW, 0f)).xyz;

                while (t < tMax)
                {
                    var positionL = light.viewOriginL + directionL * t;
                    var distance = shape.SampleDistance(positionL);
                    t += distance;
                    if (distance < tileAperture * t)
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit)
                {
                    currentLights[coneIndexG] |= lightMask;
                }
            }
        }
    }
}
