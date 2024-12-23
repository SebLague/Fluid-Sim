using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
	[ImageEffectAllowedInSceneView]
	public class RayMarchingTest : MonoBehaviour
	{
		[Header("Settings")]
		public float densityOffset = 150;
		public int numRefractions = 4;
		public Vector3 extinctionCoefficients;
		public float densityMultiplier = 0.001f;
		[Min(0.01f)] public float stepSize = 0.02f;
		public float lightStepSize = 0.4f;
		[Min(1)] public float indexOfRefraction = 1.33f;
		public Vector3 testParams;
		public EnvironmentSettings environmentSettings;

		[Header("References")]
		public FluidSim sim;
		public Transform cubeTransform;
		public Shader shader;

		Material raymarchMat;

		void Start()
		{
			raymarchMat = new Material(shader);
			Camera.main.depthTextureMode = DepthTextureMode.Depth;
		}

		[ImageEffectOpaque]
		void OnRenderImage(RenderTexture src, RenderTexture target)
		{
			if (sim.DensityMap != null)
			{
				SetShaderParams();
				Graphics.Blit(src, target, raymarchMat);
			}
			else
			{
				Graphics.Blit(src, target);
			}
		}

		void SetShaderParams()
		{
			SetEnvironmentParams(raymarchMat, environmentSettings);
			raymarchMat.SetTexture("DensityMap", sim.DensityMap);
			raymarchMat.SetVector("boundsSize", sim.Scale);
			raymarchMat.SetFloat("volumeValueOffset", densityOffset);
			raymarchMat.SetVector("testParams", testParams);
			raymarchMat.SetFloat("indexOfRefraction", indexOfRefraction);
			raymarchMat.SetFloat("densityMultiplier", densityMultiplier / 1000);
			raymarchMat.SetFloat("viewMarchStepSize", stepSize);
			raymarchMat.SetFloat("lightStepSize", lightStepSize);
			raymarchMat.SetInt("numRefractions", numRefractions);
			raymarchMat.SetVector("extinctionCoeff", extinctionCoefficients);

			raymarchMat.SetMatrix("cubeLocalToWorld", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2));
			raymarchMat.SetMatrix("cubeWorldToLocal", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2).inverse);
			
			Vector3 floorSize = new Vector3(30, 0.05f, 30);
			float floorHeight = -sim.Scale.y / 2 + sim.transform.position.y - floorSize.y/2;
			raymarchMat.SetVector("floorPos", new Vector3(0, floorHeight, 0));
			raymarchMat.SetVector("floorSize", floorSize);
		}

		public static void SetEnvironmentParams(Material mat, EnvironmentSettings environmentSettings)
		{
			mat.SetColor("tileCol1", environmentSettings.tileCol1);
			mat.SetColor("tileCol2", environmentSettings.tileCol2);
			mat.SetColor("tileCol3", environmentSettings.tileCol3);
			mat.SetColor("tileCol4", environmentSettings.tileCol4);
			mat.SetVector("tileColVariation", environmentSettings.tileColVariation);
			mat.SetFloat("tileScale", environmentSettings.tileScale);
			mat.SetFloat("tileDarkOffset", environmentSettings.tileDarkOffset);
			mat.SetVector("dirToSun", -environmentSettings.light.transform.forward);
		}

		[System.Serializable]
		public struct EnvironmentSettings
		{
			public Color tileCol1;
			public Color tileCol2;
			public Color tileCol3;
			public Color tileCol4;
			public Vector3 tileColVariation;
			public float tileScale;
			public float tileDarkOffset;
			public Light light;
		}
	}
}