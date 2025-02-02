using Seb.GPUSorting;
using UnityEngine;
using Seb.Helpers.Internal;

namespace Seb.Helpers
{
	public class SpatialHash
	{
		readonly GPUCountSort gpuSort = new();
		readonly SpatialOffsetCalculator spatialOffsetsCalc = new();

		public void Run(ComputeBuffer indexBuffer, ComputeBuffer spatialKeys, ComputeBuffer spatialOffsets)
		{
			gpuSort.Run(indexBuffer, spatialKeys, (uint)(spatialKeys.count - 1));
			spatialOffsetsCalc.Run(false, spatialKeys, spatialOffsets);
		}

		public void Release()
		{
			gpuSort.Release();
		}
	}
}