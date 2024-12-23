using Seb.Helpers;
using UnityEngine;

namespace Seb.GPUSorting
{
	public class SpatialOffsetCalculator
	{
		ComputeShader cs;

		const int initKernel = 0;
		const int offsetsKernel = 1;

		readonly int numThreadGroupsInitKernel;
		readonly int numThreadGroupsOffsetsKernel;

		public SpatialOffsetCalculator(ComputeBuffer sortedKeys, ComputeBuffer offsets)
		{
			if (sortedKeys.count != offsets.count) throw new System.Exception("Count mismatch");
			cs = ComputeHelper.LoadComputeShader("SpatialOffsets");
			ComputeHelper.SetBuffer(cs, offsets, "Offsets", initKernel, offsetsKernel);
			ComputeHelper.SetBuffer(cs, sortedKeys, "SortedKeys", offsetsKernel);
			cs.SetInt("numInputs", sortedKeys.count);

			numThreadGroupsInitKernel = ComputeHelper.CalculateThreadGroupCount1D(cs, sortedKeys.count, initKernel);
			numThreadGroupsOffsetsKernel = ComputeHelper.CalculateThreadGroupCount1D(cs, sortedKeys.count, offsetsKernel);
		}

		public void Run(bool needsInit)
		{
			if (needsInit)
			{
				cs.Dispatch(initKernel, numThreadGroupsInitKernel, 1, 1);
			}

			cs.Dispatch(offsetsKernel, numThreadGroupsOffsetsKernel, 1, 1);
		}
	}
}