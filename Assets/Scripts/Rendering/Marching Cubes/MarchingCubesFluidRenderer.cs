using UnityEngine;
using Seb.Helpers;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
    public class MarchingCubesFluidRenderer : MonoBehaviour
    {
        public float isoLevel;
        public Color col;

        [Header("References")]
        public FluidSim sim;
        public Shader drawShader;
        public ComputeShader renderArgsCompute;

        ComputeBuffer renderArgs; 
        MarchingCubes marchingCubes;
        ComputeBuffer triangleBuffer;
        Material drawMat;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

        void Start()
        {
            marchingCubes = new MarchingCubes();
        }

        void LateUpdate()
        {
            if (sim.DensityMap != null)
            {
                RenderFluid(sim.DensityMap);
            }
        }


        void RenderFluid(RenderTexture densityTexture)
        {
            // Run marching cubes compute shader and get back buffer containing triangle data
            triangleBuffer = marchingCubes.Run(densityTexture, sim.Scale, -isoLevel);
            
            if (!drawMat) drawMat = new Material(drawShader);
            // Each triangle contains 3 vertices: assign these all to the vertex buffer on the draw material
            drawMat.SetBuffer("VertexBuffer", triangleBuffer);
            drawMat.SetColor("col", col);
            
            // Create render arguments. This stores 5 values:
            // (triangle index count, instance count, sub-mesh index, base vertex index, byte offset)
            if (renderArgs == null)
            {
                renderArgs = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                renderArgsCompute.SetBuffer(0, "RenderArgs", renderArgs);
            }

            // Copy the current number of triangles from the append buffer into the render arguments.
            // (Each triangle contains 3 vertices, so we then need to multiply this value by 3 with another dispatch)
            ComputeBuffer.CopyCount(triangleBuffer, renderArgs, 0);
            renderArgsCompute.Dispatch(0, 1, 1, 1);
            
            // Draw the mesh using ProceduralIndirect to avoid having to read any data back to the CPU
            Graphics.DrawProceduralIndirect(drawMat, bounds, MeshTopology.Triangles, renderArgs);
        }

        private void OnDestroy()
        {
            Release();
        }

        void Release()
        {
            ComputeHelper.Release(renderArgs);
            marchingCubes.Release();
        }

    }
}