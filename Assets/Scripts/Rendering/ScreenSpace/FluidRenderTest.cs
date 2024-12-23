using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{
	public class FluidRenderTest : MonoBehaviour
	{
		[Header("Main Settings")] public bool useFullSizeThicknessTex;
		public Vector3 extinctionCoefficients;
		public float extinctionMultiplier;
		public float depthParticleSize;
		public float thicknessParticleScale;
		public float refractionMultiplier;
		public Vector3 testParams;

		[Header("Smoothing Settings")] public BlurType smoothType;
		public BilateralSmooth2D.BilateralFilterSettings bilateralSettings;
		public GaussSmooth.GaussianBlurSettings gaussSmoothSettings;

		[Header("Environment")] public GaussSmooth.GaussianBlurSettings shadowSmoothSettings;
		public EnvironmentSettings environmentSettings;

		[Header("Debug Settings")] public DisplayMode displayMode;
		public float depthDisplayScale;
		public float thicknessDisplayScale;

		[Header("References")] public Shader renderA;
		public Shader depthDownsampleCopyShader;
		public Shader depthShader;
		public Shader normalShader;
		public Shader thicknessShader;
		public Shader smoothThickPrepareShader;
		public FluidSim sim;
		public Camera shadowCam;
		public Light sun;
		public FoamRenderTest foamTest;

		DisplayMode displayModeOld;
		Mesh quadMesh;
		Material matDepth;
		Material matThickness;
		Material matNormal;
		Material matComposite;
		Material smoothPrepareMat;
		Material depthDownsampleCopyMat;
		ComputeBuffer argsBuffer;

		// Render textures
		RenderTexture compRt;
		RenderTexture depthRt;
		RenderTexture normalRt;
		RenderTexture shadowRt;
		RenderTexture foamRt;
		RenderTexture thicknessRt;

		// Command buffers
		CommandBuffer cmd;
		CommandBuffer shadowCmd;

		// Smoothing types
		Bilateral1D bilateral1D = new();
		BilateralSmooth2D bilateral2D = new();
		GaussSmooth gaussSmooth = new();

		void Update()
		{
			Init();
			RenderCamSetup();
			ShadowCamSetup();
			BuildCommands();
			UpdateSettings();

			HandleDebugDisplayInput();
		}

		void BuildCommands()
		{
			// ---- Shadow cmds ----
			shadowCmd.Clear();
			shadowCmd.SetRenderTarget(shadowRt);
			shadowCmd.ClearRenderTarget(true, true, Color.black);
			shadowCmd.DrawMeshInstancedIndirect(quadMesh, 0, matThickness, 0, argsBuffer);
			gaussSmooth.Smooth(shadowCmd, shadowRt, shadowRt, shadowRt.descriptor, shadowSmoothSettings, Vector3.one);

			// ---- Render commands ----
			cmd.Clear();

			// -- Render foam/spray/bubbles: rgb = (foam, foamDepth_unity, foamDepth_linear) --
			cmd.SetRenderTarget(foamRt);
			float depthClearVal = SystemInfo.usesReversedZBuffer ? 0 : 1;
			cmd.ClearRenderTarget(true, true, new Color(0, depthClearVal, 0, 0));
			foamTest.RenderWithCmdBuffer(cmd);

			// -- Render particles to Depth texture --
			cmd.SetRenderTarget(depthRt);
			cmd.ClearRenderTarget(true, true, Color.white * 10000000, 1);
			cmd.DrawMeshInstancedIndirect(quadMesh, 0, matDepth, 0, argsBuffer);

			// -- Render particles to thickness texture --
			cmd.SetRenderTarget(thicknessRt);
			cmd.Blit(foamRt, thicknessRt, depthDownsampleCopyMat); // copy depth from foamRt into the thicknessRt depth buffer
			cmd.DrawMeshInstancedIndirect(quadMesh, 0, matThickness, 0, argsBuffer);

			// ---- Pack thickness and depth into compRt (depth, thick, thick, depth) ----
			cmd.Blit(null, compRt, smoothPrepareMat);

			// -- Apply smoothing to RG channels of compRt, using A channel as depth source --
			// After smoothing, it will contain (thickness_smooth, thickness, depth)
			ApplyActiveSmoothingType(cmd, compRt, compRt, compRt.descriptor, new Vector3(1, 1, 0));

			// -- Reconstruct normals from smooth depth --
			cmd.Blit(compRt, normalRt, matNormal);

			// -- Composite final image and draw to screen --
			cmd.Blit(foamRt, BuiltinRenderTextureType.CameraTarget, matComposite);
		}

		void Init()
		{
			if (!quadMesh) quadMesh = QuadGenerator.GenerateQuadMesh();
			ComputeHelper.CreateArgsBuffer(ref argsBuffer, quadMesh, sim.positionBuffer.count);

			InitTextures();
			InitMaterials();

			void InitMaterials()
			{
				if (!depthDownsampleCopyMat) depthDownsampleCopyMat = new Material(depthDownsampleCopyShader);
				if (!matDepth) matDepth = new Material(depthShader);
				if (!matNormal) matNormal = new Material(normalShader);
				if (!matThickness) matThickness = new Material(thicknessShader);
				if (!smoothPrepareMat) smoothPrepareMat = new Material(smoothThickPrepareShader);
				if (!matComposite) matComposite = new Material(renderA);
			}

			void InitTextures()
			{
				// Display size
				int width = Screen.width;
				int height = Screen.height;

				// Thickness texture size
				float aspect = height / (float)width;
				int thicknessTexMaxWidth = Mathf.Min(1280, width);
				int thicknessTexMaxHeight = Mathf.Min((int)(1280 * aspect), height);
				int thicknessTexWidth = Mathf.Max(thicknessTexMaxWidth, width / 2);
				int thicknessTexHeight = Mathf.Max(thicknessTexMaxHeight, height / 2);

				if (useFullSizeThicknessTex)
				{
					thicknessTexWidth = width;
					thicknessTexHeight = height;
				}

				// Shadow texture size
				const int shadowTexSizeReduction = 4;
				int shadowTexWidth = width / shadowTexSizeReduction;
				int shadowTexHeight = height / shadowTexSizeReduction;

				GraphicsFormat fmtRGBA = GraphicsFormat.R32G32B32A32_SFloat;
				GraphicsFormat fmtR = GraphicsFormat.R32_SFloat;
				ComputeHelper.CreateRenderTexture(ref depthRt, width, height, FilterMode.Bilinear, fmtR, depthMode: DepthMode.Depth16);
				ComputeHelper.CreateRenderTexture(ref thicknessRt, thicknessTexWidth, thicknessTexHeight, FilterMode.Bilinear, fmtR, depthMode: DepthMode.Depth16);
				ComputeHelper.CreateRenderTexture(ref normalRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.None);
				ComputeHelper.CreateRenderTexture(ref compRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.None);
				ComputeHelper.CreateRenderTexture(ref shadowRt, shadowTexWidth, shadowTexHeight, FilterMode.Bilinear, fmtR, depthMode: DepthMode.None);
				ComputeHelper.CreateRenderTexture(ref foamRt, width, height, FilterMode.Bilinear, fmtRGBA, depthMode: DepthMode.Depth16);
			}
		}


		void ApplyActiveSmoothingType(CommandBuffer cmd, RenderTargetIdentifier src, RenderTargetIdentifier target, RenderTextureDescriptor desc, Vector3 smoothMask)
		{
			if (smoothType == BlurType.Bilateral1D)
			{
				bilateral1D.Smooth(cmd, src, target, desc, bilateralSettings, smoothMask);
			}
			else if (smoothType == BlurType.Bilateral2D)
			{
				bilateral2D.Smooth(cmd, src, target, desc, bilateralSettings, smoothMask);
			}
			else if (smoothType == BlurType.Gaussian)
			{
				gaussSmooth.Smooth(cmd, src, target, desc, gaussSmoothSettings, smoothMask);
			}
		}

		float FrameBoundsOrtho(Vector3 boundsSize, Matrix4x4 worldToView)
		{
			Vector3 halfSize = boundsSize * 0.5f;
			float maxX = 0;
			float maxY = 0;

			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3(
					(i & 1) == 0 ? -halfSize.x : halfSize.x,
					(i & 2) == 0 ? -halfSize.y : halfSize.y,
					(i & 4) == 0 ? -halfSize.z : halfSize.z
				);

				Vector3 viewCorner = worldToView.MultiplyPoint(corner);
				maxX = Mathf.Max(maxX, Mathf.Abs(viewCorner.x));
				maxY = Mathf.Max(maxY, Mathf.Abs(viewCorner.y));
			}

			float aspect = Screen.height / (float)Screen.width;
			float targetOrtho = Mathf.Max(maxY, maxX * aspect);
			return targetOrtho;
		}

		void RenderCamSetup()
		{
			if (cmd == null)
			{
				cmd = new();
				cmd.name = "Fluid Render Commands";
			}

			Camera.main.RemoveAllCommandBuffers();
			Camera.main.AddCommandBuffer(CameraEvent.AfterEverything, cmd);
			Camera.main.depthTextureMode = DepthTextureMode.Depth;
		}

		void ShadowCamSetup()
		{
			if (shadowCmd == null)
			{
				shadowCmd = new();
				shadowCmd.name = "Fluid Shadow Render Commands";
			}

			shadowCam.RemoveAllCommandBuffers();
			shadowCam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, shadowCmd);

			Vector3 dirToSun = -sun.transform.forward;
			shadowCam.transform.position = dirToSun * 50;
			shadowCam.transform.rotation = sun.transform.rotation;
			shadowCam.orthographicSize = FrameBoundsOrtho(sim.Scale, shadowCam.worldToCameraMatrix) + 0.5f;
		}


		void UpdateSettings()
		{
			// ---- Smooth prepare ----
			smoothPrepareMat.SetTexture("Depth", depthRt);
			smoothPrepareMat.SetTexture("Thick", thicknessRt);
			
			// ---- Thickess ----
			matThickness.SetBuffer("Positions", sim.positionBuffer);
			matThickness.SetFloat("scale", thicknessParticleScale);
			
			// ---- Depth ----
			matDepth.SetBuffer("Positions", sim.positionBuffer);
			matDepth.SetFloat("scale", depthParticleSize);

			// ---- Normals ----
			matNormal.SetInt("useSmoothedDepth", Input.GetKey(KeyCode.LeftControl) ? 0 : 1);

			// ---- Composite mat settings ----
			matComposite.SetInt("debugDisplayMode", (int)displayMode);
			matComposite.SetTexture("Comp", compRt);
			matComposite.SetTexture("Normals", normalRt);
			matComposite.SetTexture("ShadowMap", shadowRt);
			
			matComposite.SetVector("testParams", testParams);
			matComposite.SetVector("extinctionCoefficients", extinctionCoefficients * extinctionMultiplier);
			matComposite.SetVector("boundsSize", sim.Scale);
			matComposite.SetFloat("refractionMultiplier", refractionMultiplier);

			matComposite.SetMatrix("shadowVP", GL.GetGPUProjectionMatrix(shadowCam.projectionMatrix, false) * shadowCam.worldToCameraMatrix);
			matComposite.SetVector("dirToSun", -sun.transform.forward);
			matComposite.SetFloat("depthDisplayScale", depthDisplayScale);
			matComposite.SetFloat("thicknessDisplayScale", thicknessDisplayScale);
			matComposite.SetBuffer("foamCountBuffer", sim.foamCountBuffer);
			matComposite.SetInt("foamMax", sim.foamBuffer.count);
			
			// Environment
			Vector3 floorSize = new Vector3(30, 0.05f, 30);
			float floorHeight = -sim.Scale.y / 2 + sim.transform.position.y - floorSize.y / 2;
			matComposite.SetVector("floorPos", new Vector3(0, floorHeight, 0));
			matComposite.SetVector("floorSize", floorSize);
			matComposite.SetColor("tileCol1", environmentSettings.tileCol1);
			matComposite.SetColor("tileCol2", environmentSettings.tileCol2);
			matComposite.SetColor("tileCol3", environmentSettings.tileCol3);
			matComposite.SetColor("tileCol4", environmentSettings.tileCol4);
			matComposite.SetVector("tileColVariation", environmentSettings.tileColVariation);
			matComposite.SetFloat("tileScale", environmentSettings.tileScale);
			matComposite.SetFloat("tileDarkOffset", environmentSettings.tileDarkOffset);
			matComposite.SetFloat("sunIntensity", environmentSettings.sunIntensity);
			matComposite.SetFloat("sunInvSize", environmentSettings.sunInvSize);
		}

		void HandleDebugDisplayInput()
		{
			// -- Set display mode with num keys --
			for (int i = 0; i <= 9; i++)
			{
				if (Input.GetKeyDown(KeyCode.Alpha0 + i))
				{
					displayMode = (DisplayMode)i;
					Debug.Log("Set display mode: " + displayMode);
				}
			}
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
			public float sunIntensity;
			public float sunInvSize;
		}

		public enum DisplayMode
		{
			Composite,
			Depth,
			SmoothDepth,
			Normal,
			Thickness,
			SmoothThickness
		}

		public enum BlurType
		{
			Gaussian,
			Bilateral2D,
			Bilateral1D
		}


		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(depthRt, thicknessRt, normalRt, compRt, shadowRt, foamRt);
		}
	}
}