using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Seb.Fluid.Rendering
{
	public class BilateralSmooth2D
	{
		Material mat;

		readonly int tempRT;

		public BilateralSmooth2D()
		{
			tempRT = Shader.PropertyToID("BilateralFilter_TempRT_ID");
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, BilateralFilterSettings settings)
		{
			Smooth(cmd, source, target, desc, settings, Vector3.one);
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, BilateralFilterSettings settings, Vector3 smoothMask)
		{
			if (mat == null)
			{
				mat = new Material(Shader.Find("Hidden/BilateralFilter2D"));
			}


			mat.SetFloat("worldRadius", settings.WorldRadius);
			mat.SetInt("maxScreenSpaceRadius", settings.MaxScreenSpaceSize);
			mat.SetFloat("strength", settings.Strength);
			mat.SetFloat("diffStrength", settings.DiffStrength);
			mat.SetVector("smoothMask", smoothMask);

			cmd.GetTemporaryRT(tempRT, desc);

			RenderTargetIdentifier rtA = source;
			RenderTargetIdentifier rtB = tempRT;

			for (int i = 0; i < settings.Iterations; i++)
			{
				cmd.Blit(rtA, rtB, mat);
				(rtA, rtB) = (rtB, rtA);
			}

			cmd.Blit(rtA, target);

			cmd.ReleaseTemporaryRT(tempRT);
		}

		[System.Serializable]
		public struct BilateralFilterSettings
		{
			public float WorldRadius;

			[FormerlySerializedAs("MaxScreenSpaceRadius")]
			public int MaxScreenSpaceSize;

			[Range(0, 1)] public float Strength;
			public float DiffStrength;
			public int Iterations;
		}
	}
}