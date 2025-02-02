using Seb.GPUSorting;
using UnityEngine;
using Seb.Helpers.Internal;

namespace Seb.Helpers
{
	public class SpatialHash
	{
		public ComputeBuffer SpatialKeys;
		public ComputeBuffer SpatialIndices;
		public ComputeBuffer SpatialOffsets;

		readonly GPUCountSort gpuSort = new();
		readonly SpatialOffsetCalculator spatialOffsetsCalc = new();

		public SpatialHash(int size)
		{
			CreateBuffers(size);
		}

		public void Resize(int newSize)
		{
			CreateBuffers(newSize);
		}

		public void Run()
		{
			gpuSort.Run(SpatialIndices, SpatialKeys, (uint)(SpatialKeys.count - 1));
			spatialOffsetsCalc.Run(false, SpatialKeys, SpatialOffsets);
		}

		public void Release()
		{
			gpuSort.Release();
			ComputeHelper.Release(SpatialKeys, SpatialIndices, SpatialOffsets);
		}

		void CreateBuffers(int count)
		{
			ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialKeys, count);
			ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialIndices, count);
			ComputeHelper.CreateStructuredBuffer<uint>(ref SpatialOffsets, count);
		}
	}
}