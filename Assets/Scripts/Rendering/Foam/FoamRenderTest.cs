using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
	public class FoamRenderTest : MonoBehaviour
	{
		public float scale;
		public float debugParam;
		public bool autoDraw;

		[Header("References")]
		public Shader shaderBillboard;
		public ComputeShader copyCountToArgsCompute;

		FluidSim sim;
		Material mat;
		Mesh mesh;
		ComputeBuffer argsBuffer;
		Bounds bounds;

		void Awake()
		{
			sim = FindObjectOfType<FluidSim>();
			sim.SimulationInitCompleted += Init;
		}

		void Init(FluidSim sim)
		{
			mat = new Material(shaderBillboard);
			mesh = QuadGenerator.GenerateQuadMesh();
			bounds = new Bounds(Vector3.zero, Vector3.one * 1000);

			ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.maxFoamParticleCount);
			copyCountToArgsCompute.SetBuffer(0, "CountBuffer", sim.foamCountBuffer);
			copyCountToArgsCompute.SetBuffer(0, "ArgsBuffer", argsBuffer);
			mat.SetBuffer("Particles", sim.foamBuffer);
		}

		void LateUpdate()
		{
			if (sim.foamActive)
			{
				mat.SetFloat("debugParam", debugParam);
				mat.SetInt("bubbleClassifyMinNeighbours", sim.bubbleClassifyMinNeighbours);
				mat.SetInt("sprayClassifyMaxNeighbours", sim.sprayClassifyMaxNeighbours);
				mat.SetFloat("scale", scale * 0.01f);

				if (autoDraw)
				{
					copyCountToArgsCompute.Dispatch(0, 1, 1, 1);
					Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
				}
			}
		}

		public void RenderWithCmdBuffer(CommandBuffer cmd)
		{
			cmd.DispatchCompute(copyCountToArgsCompute, 0, 1, 1, 1);
			cmd.DrawMeshInstancedIndirect(mesh, 0, mat, 0, argsBuffer);
		}


		private void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
		}
	}
}