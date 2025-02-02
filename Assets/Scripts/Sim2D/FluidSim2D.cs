using Seb.Fluid2D.Rendering;
using Seb.Helpers;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Serialization;

namespace Seb.Fluid2D.Simulation
{
	public class FluidSim2D : MonoBehaviour
	{
		public event System.Action SimulationStepCompleted;

		[Header("Simulation Settings")]
		public float timeScale = 1;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame;
		public float gravity;
		[Range(0, 1)] public float collisionDamping = 0.95f;
		public float smoothingRadius = 2;
		public float targetDensity;
		public float pressureMultiplier;
		public float nearPressureMultiplier;
		public float viscosityStrength;
		public Vector2 boundsSize;
		public Vector2 obstacleSize;
		public Vector2 obstacleCentre;

		[Header("Interaction Settings")]
		public float interactionRadius;

		public float interactionStrength;

		[Header("References")]
		public ComputeShader compute;

		public Spawner2D spawner2D;

		// Buffers
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }

		ComputeBuffer sortTarget_Position;
		ComputeBuffer sortTarget_PredicitedPosition;
		ComputeBuffer sortTarget_Velocity;

		ComputeBuffer predictedPositionBuffer;
		SpatialHash spatialHash;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int copybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionKernel = 7;

		// State
		bool isPaused;
		Spawner2D.ParticleSpawnData spawnData;
		bool pauseNextFrame;

		public int numParticles { get; private set; }


		void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");

			Init();
		}

		void Init()
		{
			float deltaTime = 1 / 60f;
			Time.fixedDeltaTime = deltaTime;

			spawnData = spawner2D.GetSpawnData();
			numParticles = spawnData.positions.Length;
			spatialHash = new SpatialHash(numParticles);

			// Create buffers
			positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

			sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

			// Set buffer data
			SetInitialBufferData(spawnData);

			// Init compute
			ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);

			ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);

			ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);

			compute.SetInt("numParticles", numParticles);
		}


		void Update()
		{
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame)
			{
				isPaused = true;
				pauseNextFrame = false;
			}

			HandleInput();
		}

		void RunSimulationFrame(float frameTime)
		{
			float timeStep = frameTime / iterationsPerFrame;

			UpdateSettings(timeStep);

			for (int i = 0; i < iterationsPerFrame; i++)
			{
				RunSimulationStep();
				SimulationStepCompleted?.Invoke();
			}
		}

		void RunSimulationStep()
		{
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);

			RunSpatial();

			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
		}

		void RunSpatial()
		{
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
			spatialHash.Run();

			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
		}

		void UpdateSettings(float deltaTime)
		{
			compute.SetFloat("deltaTime", deltaTime);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetVector("boundsSize", boundsSize);
			compute.SetVector("obstacleSize", obstacleSize);
			compute.SetVector("obstacleCentre", obstacleCentre);

			compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
			compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
			compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
			compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
			compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

			// Mouse interaction settings:
			Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			bool isPullInteraction = Input.GetMouseButton(0);
			bool isPushInteraction = Input.GetMouseButton(1);
			float currInteractStrength = 0;
			if (isPushInteraction || isPullInteraction)
			{
				currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
			}

			compute.SetVector("interactionInputPoint", mousePos);
			compute.SetFloat("interactionInputStrength", currInteractStrength);
			compute.SetFloat("interactionInputRadius", interactionRadius);
		}

		void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
		{
			float2[] allPoints = new float2[spawnData.positions.Length]; //
			System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

			positionBuffer.SetData(allPoints);
			predictedPositionBuffer.SetData(allPoints);
			velocityBuffer.SetData(spawnData.velocities);
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				isPaused = !isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				isPaused = false;
				pauseNextFrame = true;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				isPaused = true;
				// Reset positions, the run single frame to get density etc (for debug purposes) and then reset positions again
				SetInitialBufferData(spawnData);
				RunSimulationStep();
				SetInitialBufferData(spawnData);
			}
		}


		void OnDestroy()
		{
			ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer, sortTarget_Position, sortTarget_Velocity, sortTarget_PredicitedPosition);
			spatialHash.Release();
		}


		void OnDrawGizmos()
		{
			Gizmos.color = new Color(0, 1, 0, 0.4f);
			Gizmos.DrawWireCube(Vector2.zero, boundsSize);
			Gizmos.DrawWireCube(obstacleCentre, obstacleSize);

			if (Application.isPlaying)
			{
				Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				bool isPullInteraction = Input.GetMouseButton(0);
				bool isPushInteraction = Input.GetMouseButton(1);
				bool isInteracting = isPullInteraction || isPushInteraction;
				if (isInteracting)
				{
					Gizmos.color = isPullInteraction ? Color.green : Color.red;
					Gizmos.DrawWireSphere(mousePos, interactionRadius);
				}
			}
		}
	}
}