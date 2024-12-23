using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Seb.Helpers;

namespace Seb.GPUSorting
{
	public class GPUCountSort
	{
		readonly Scan scan = new();
		readonly ComputeShader cs;

		readonly ComputeBuffer sortedKeysBuffer;
		readonly ComputeBuffer sortedValuesBuffer;
		readonly ComputeBuffer countsBuffer;

		const int ClearCountsKernel = 0;
		const int CountKernel = 1;
		const int ScatterOutputsKernel = 2;
		const int CopyBackKernel = 3;

		// Sorts a buffer of keys based on a buffer of values (note that value buffer will also be sorted in the process).
		// Note: the maximum possible value must be known ahead of time for this algorithm (and preferably not be too large), as memory is allocated for all possible values.
		// Both buffers expected to be of type <uint>
		// For sorting of other data types, the key buffer can just contain indices 0 to n, and once those have been sorted, they can be used to index/reorder the actual data.
		public GPUCountSort(ComputeBuffer keysBuffer, ComputeBuffer valuesBuffer, uint maxValue)
		{
			int count = keysBuffer.count;
			cs = ComputeHelper.LoadComputeShader("CountSortA");

			sortedKeysBuffer = ComputeHelper.CreateStructuredBuffer<uint>(count);
			sortedValuesBuffer = ComputeHelper.CreateStructuredBuffer<uint>(count);
			countsBuffer = ComputeHelper.CreateStructuredBuffer<uint>((int)maxValue + 1);

			ComputeHelper.SetBuffer(cs, keysBuffer, "InputKeys", CountKernel, ScatterOutputsKernel, CopyBackKernel);
			ComputeHelper.SetBuffer(cs, valuesBuffer, "InputValues", ScatterOutputsKernel, CopyBackKernel);

			ComputeHelper.SetBuffer(cs, sortedKeysBuffer, "SortedKeys", ScatterOutputsKernel, CopyBackKernel);
			ComputeHelper.SetBuffer(cs, sortedValuesBuffer, "SortedValues", ScatterOutputsKernel, CopyBackKernel);
			ComputeHelper.SetBuffer(cs, countsBuffer, "Counts", ClearCountsKernel, CountKernel, ScatterOutputsKernel);
			cs.SetInt("numInputs", count);
		}

		public void Run()
		{
			int count = sortedKeysBuffer.count;

			ComputeHelper.Dispatch(cs, count, kernelIndex: ClearCountsKernel);
			ComputeHelper.Dispatch(cs, count, kernelIndex: CountKernel);

			scan.Run(countsBuffer);
			ComputeHelper.Dispatch(cs, count, kernelIndex: ScatterOutputsKernel);
			ComputeHelper.Dispatch(cs, count, kernelIndex: CopyBackKernel);
		}

		public void Release()
		{
			ComputeHelper.Release(sortedKeysBuffer, sortedValuesBuffer, countsBuffer);
			scan.Release();
		}
	}
}