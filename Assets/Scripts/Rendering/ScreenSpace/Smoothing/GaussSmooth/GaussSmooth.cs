using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid.Rendering
{
    public class GaussSmooth
    {
        Material mat;
        readonly int firstPassRT;

        public GaussSmooth()
        {
            firstPassRT = Shader.PropertyToID("GaussSmooth_FirstPassRT_ID");
        }

        public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, GaussianBlurSettings settings)
        {
            Smooth(cmd, source, target, desc, settings, Vector3.one);
        }

        public void Smooth(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, RenderTextureDescriptor desc, GaussianBlurSettings settings, Vector3 smoothMask)
        {
            if (mat == null)
            {
                mat = new Material(Shader.Find("Hidden/GaussSmooth"));
            }

            // Set material properties
            mat.SetFloat("radius", settings.Radius);
            mat.SetInt("maxScreenSpaceRadius", settings.MaxScreenSpaceRadius);
            mat.SetFloat("strength", settings.Strength);
            mat.SetVector("smoothMask", smoothMask);
            mat.SetInt("useWorldSpaceRadius", settings.UseWorldSpaceRadius ? 1 : 0);

            // Get temp rt for first pass
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

        [System.Serializable]
        public struct GaussianBlurSettings
        {
            public bool UseWorldSpaceRadius;
            public float Radius;
            public int MaxScreenSpaceRadius;
            [Range(0, 1)] public float Strength;
            public int Iterations;
        }

    }
}