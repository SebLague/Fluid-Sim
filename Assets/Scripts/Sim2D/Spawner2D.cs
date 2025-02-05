using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class Spawner2D : MonoBehaviour
{
	public float spawnDensity;

	public Vector2 initialVelocity;
	public float jitterStr;
	public SpawnRegion[] spawnRegions;
	public bool showSpawnBoundsGizmos;

	[Header("Debug Info")]
	public int spawnParticleCount;

	public ParticleSpawnData GetSpawnData()
	{
		var rng = new Unity.Mathematics.Random(42);

		List<float2> allPoints = new();
		List<float2> allVelocities = new();
		List<int> allIndices = new();

		for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
		{
			SpawnRegion region = spawnRegions[regionIndex];
			float2[] points = SpawnInRegion(region);

			for (int i = 0; i < points.Length; i++)
			{
				float angle = (float)rng.NextDouble() * 3.14f * 2;
				float2 dir = new float2(Mathf.Cos(angle), Mathf.Sin(angle));
				float2 jitter = dir * jitterStr * ((float)rng.NextDouble() - 0.5f);
				allPoints.Add(points[i] + jitter);
				allVelocities.Add(initialVelocity);
				allIndices.Add(regionIndex);
			}
		}

		ParticleSpawnData data = new()
		{
			positions = allPoints.ToArray(),
			velocities = allVelocities.ToArray(),
			spawnIndices = allIndices.ToArray(),
		};

		return data;
	}

	float2[] SpawnInRegion(SpawnRegion region)
	{
		Vector2 centre = region.position;
		Vector2 size = region.size;
		int i = 0;
		Vector2Int numPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, spawnDensity);
		float2[] points = new float2[numPerAxis.x * numPerAxis.y];

		for (int y = 0; y < numPerAxis.y; y++)
		{
			for (int x = 0; x < numPerAxis.x; x++)
			{
				float tx = x / (numPerAxis.x - 1f);
				float ty = y / (numPerAxis.y - 1f);

				float px = (tx - 0.5f) * size.x + centre.x;
				float py = (ty - 0.5f) * size.y + centre.y;
				points[i] = new float2(px, py);
				i++;
			}
		}

		return points;
	}

	static Vector2Int CalculateSpawnCountPerAxisBox2D(Vector2 size, float spawnDensity)
	{
		float area = size.x * size.y;
		int targetTotal = Mathf.CeilToInt(area * spawnDensity);

		float lenSum = size.x + size.y;
		Vector2 t = size / lenSum;
		float m = Mathf.Sqrt(targetTotal / (t.x * t.y));
		int nx = Mathf.CeilToInt(t.x * m);
		int ny = Mathf.CeilToInt(t.y * m);

		return new Vector2Int(nx, ny);
	}

	public struct ParticleSpawnData
	{
		public float2[] positions;
		public float2[] velocities;
		public int[] spawnIndices;

		public ParticleSpawnData(int num)
		{
			positions = new float2[num];
			velocities = new float2[num];
			spawnIndices = new int[num];
		}
	}

	[System.Serializable]
	public struct SpawnRegion
	{
		public Vector2 position;
		public Vector2 size;
		public Color debugCol;
	}

	void OnValidate()
	{
		spawnParticleCount = 0;
		foreach (SpawnRegion region in spawnRegions)
		{
			Vector2Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, spawnDensity);
			spawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y;
		}
	}

	void OnDrawGizmos()
	{
		if (showSpawnBoundsGizmos && !Application.isPlaying)
		{
			foreach (SpawnRegion region in spawnRegions)
			{
				Gizmos.color = region.debugCol;
				Gizmos.DrawWireCube(region.position, region.size);
			}
		}
	}
}