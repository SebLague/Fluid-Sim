using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid.Rendering
{
	public class Bilateral1D
	{
		Material mat;
		readonly int firstPassRT;

		public Bilateral1D()
		{
			firstPassRT = Shader.PropertyToID("BilateralFilter1D_TempRT_ID");
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, BilateralSmooth2D.BilateralFilterSettings settings)
		{
			Smooth(cmd, source, target, desc, settings, Vector3.one);
		}

		public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, BilateralSmooth2D.BilateralFilterSettings settings, Vector3 smoothMask)
		{
			if (mat == null)
			{
				mat = new Material(Shader.Find("Hidden/BilateralFilter1D"));
			}

			mat.SetFloat("worldRadius", settings.WorldRadius);
			mat.SetInt("maxScreenSpaceRadius", settings.MaxScreenSpaceSize);
			mat.SetFloat("strength", settings.Strength);
			mat.SetFloat("diffStrength", settings.DiffStrength);
			mat.SetVector("smoothMask", smoothMask);

			cmd.GetTemporaryRT(firstPassRT, desc);

			for (int i = 0; i < settings.Iterations; i++)
			{
				// First pass
				cmd.Blit(source, firstPassRT, mat, 0);
				// Second pass
				cmd.Blit(firstPassRT, target, mat, 1);

				source = target;
			}

			// Cleanup
			cmd.ReleaseTemporaryRT(firstPassRT);
		}
	}
}