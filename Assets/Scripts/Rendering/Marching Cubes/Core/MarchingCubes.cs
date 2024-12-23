using System;
using UnityEngine;
using Seb.Helpers;
using System.Linq;

namespace Seb.Fluid.Rendering
{

    public class MarchingCubes
    {
        readonly ComputeShader marchingCubesCS;
        readonly ComputeBuffer lutBuffer;
        ComputeBuffer triangleBuffer;

        public MarchingCubes()
        {
            marchingCubesCS = Resources.Load<ComputeShader>("MarchingCubes");
            string lutString = Resources.Load<TextAsset>("MarchingCubesLUT").text;
            int[] lutVals = lutString.Trim().Split(',').Select(x => int.Parse(x)).ToArray();
            lutBuffer = ComputeHelper.CreateStructuredBuffer(lutVals);

        }

        void ApplyComputeSettings(RenderTexture densityMap, Vector3 scale, float isoLevel, ComputeBuffer triangleBuffer)
        {
            marchingCubesCS.SetBuffer(0, "triangles", triangleBuffer);
            marchingCubesCS.SetBuffer(0, "lut", lutBuffer);

            marchingCubesCS.SetTexture(0, "DensityMap", densityMap);
            marchingCubesCS.SetInts("densityMapSize", densityMap.width, densityMap.height, densityMap.volumeDepth);
            marchingCubesCS.SetFloat("isoLevel", isoLevel);
            marchingCubesCS.SetVector("scale", scale);
        }

        public ComputeBuffer Run(RenderTexture densityTexture, Vector3 scale, float isoLevel)
        {
            CreateTriangleBuffer(densityTexture.width);
            ApplyComputeSettings(densityTexture, scale, isoLevel, triangleBuffer);

            int numVoxelsPerX = densityTexture.width - 1;
            int numVoxelsPerY = densityTexture.height - 1;
            int numVoxelsPerZ = densityTexture.volumeDepth - 1;
            ComputeHelper.Dispatch(marchingCubesCS, numVoxelsPerX, numVoxelsPerY, numVoxelsPerZ, 0);

            return triangleBuffer;
        }

        void CreateTriangleBuffer(int resolution, bool warnIfExceedsMaxTheoreticalSize = false)
        {
            // ComputeHelper.Release(triangleBuffer);
            int numVoxelsPerAxis = resolution - 1;
            int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
            int maxTriangleCount = numVoxels * 5;
            int byteSize = ComputeHelper.GetStride<Triangle>();
            const uint maxBytes = 2147483648;
            uint maxEntries = maxBytes / (uint)byteSize;
            if (maxEntries < maxTriangleCount && warnIfExceedsMaxTheoreticalSize)
            {
                Debug.Log("Max theoretical triangle count too large for buffer. Clamping length.");
            }

            ComputeHelper.CreateAppendBuffer<Triangle>(ref triangleBuffer, Math.Min((int)maxEntries, maxTriangleCount));
        }

        public void Release()
        {
            ComputeHelper.Release(triangleBuffer, lutBuffer);
        }


        public struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
        } //

        public struct Triangle
        {
            public Vertex vertexA;
            public Vertex vertexB;
            public Vertex vertexC;
        }
    }
}