using UnityEngine;

public class ParticleDisplay2D : MonoBehaviour
{
	public Mesh mesh;
	public Shader shader;
	public float scale;
	public Gradient colourMap;
	public int gradientResolution;
	public float velocityDisplayMax;

	Material material;
	ComputeBuffer argsBuffer;
	Bounds bounds;
	Texture2D gradientTexture;
	bool needsUpdate;


	public void Init(Simulation2D sim)
	{
		material = new Material(shader);
		material.SetBuffer("Positions2D", sim.positionBuffer);
		material.SetBuffer("Velocities", sim.velocityBuffer);
		material.SetBuffer("DensityData", sim.densityBuffer);

		argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.positionBuffer.count);
		bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
	}

	void LateUpdate()
	{
		if (shader != null)
		{
			UpdateSettings();
			Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
		}
	}

	void UpdateSettings()
	{
		if (needsUpdate)
		{
			needsUpdate = false;
			TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
			material.SetTexture("ColourMap", gradientTexture);

			material.SetFloat("scale", scale);
			material.SetFloat("velocityMax", velocityDisplayMax);
		}
	}

	public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
	{
		if (texture == null)
		{
			texture = new Texture2D(width, 1);
		}
		else if (texture.width != width)
		{
			texture.Reinitialize(width, 1);
		}
		if (gradient == null)
		{
			gradient = new Gradient();
			gradient.SetKeys(
				new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
				new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
			);
		}
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = filterMode;

		Color[] cols = new Color[width];
		for (int i = 0; i < cols.Length; i++)
		{
			float t = i / (cols.Length - 1f);
			cols[i] = gradient.Evaluate(t);
		}
		texture.SetPixels(cols);
		texture.Apply();
	}

	void OnValidate()
	{
		needsUpdate = true;
	}

	void OnDestroy()
	{
		ComputeHelper.Release(argsBuffer);
	}
}
