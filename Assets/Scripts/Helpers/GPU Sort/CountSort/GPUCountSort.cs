using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Seb.Helpers;

namespace Seb.GPUSorting
{
	public class GPUCountSort
	{
		static readonly int ID_InputItems = Shader.PropertyToID("InputItems");
		static readonly int ID_InputSortKeys = Shader.PropertyToID("InputKeys");
		static readonly int ID_SortedItems = Shader.PropertyToID("SortedItems");
		static readonly int ID_SortedKeys = Shader.PropertyToID("SortedKeys");
		static readonly int ID_Counts = Shader.PropertyToID("Counts");
		static readonly int ID_NumInputs = Shader.PropertyToID("numInputs");

		readonly Scan scan = new();
		readonly ComputeShader cs = ComputeHelper.LoadComputeShader("CountSort");

		ComputeBuffer sortedItemsBuffer;
		ComputeBuffer sortedValuesBuffer;
		ComputeBuffer countsBuffer;

		const int ClearCountsKernel = 0;
		const int CountKernel = 1;
		const int ScatterOutputsKernel = 2;
		const int CopyBackKernel = 3;

		// Sorts a buffer of indices based on a buffer of keys (note that the keys will also be sorted in the process).
		// Note: the maximum possible key value must be known ahead of time for this algorithm (and preferably not be too large), as memory is allocated for all possible keys.
		// Both buffers expected to be of type <uint>
		// Note: index buffer is initialized here to values 0...n before sorting

		public void Run(ComputeBuffer itemsBuffer, ComputeBuffer keysBuffer, uint maxValue)
		{
			// ---- Init ----
			int count = itemsBuffer.count;
			if (ComputeHelper.CreateStructuredBuffer<uint>(ref sortedItemsBuffer, count))
			{
				cs.SetBuffer(ScatterOutputsKernel, ID_SortedItems, sortedItemsBuffer);
				cs.SetBuffer(CopyBackKernel, ID_SortedItems, sortedItemsBuffer);
			}

			if (ComputeHelper.CreateStructuredBuffer<uint>(ref sortedValuesBuffer, count))
			{
				cs.SetBuffer(ScatterOutputsKernel, ID_SortedKeys, sortedValuesBuffer);
				cs.SetBuffer(CopyBackKernel, ID_SortedKeys, sortedValuesBuffer);
			}

			if (ComputeHelper.CreateStructuredBuffer<uint>(ref countsBuffer, (int)maxValue + 1))
			{
				cs.SetBuffer(ClearCountsKernel, ID_Counts, countsBuffer);
				cs.SetBuffer(CountKernel, ID_Counts, countsBuffer);
				cs.SetBuffer(ScatterOutputsKernel, ID_Counts, countsBuffer);
			}

			cs.SetBuffer(ClearCountsKernel, ID_InputItems, itemsBuffer);
			cs.SetBuffer(CountKernel, ID_InputSortKeys, keysBuffer);
			cs.SetBuffer(ScatterOutputsKernel, ID_InputItems, itemsBuffer);
			cs.SetBuffer(CopyBackKernel, ID_InputItems, itemsBuffer);

			cs.SetBuffer(ScatterOutputsKernel, ID_InputSortKeys, keysBuffer);
			cs.SetBuffer(CopyBackKernel, ID_InputSortKeys, keysBuffer);

			cs.SetInt(ID_NumInputs, count);

			// ---- Run ----
			ComputeHelper.Dispatch(cs, count, kernelIndex: ClearCountsKernel);
			ComputeHelper.Dispatch(cs, count, kernelIndex: CountKernel);

			scan.Run(countsBuffer);
			ComputeHelper.Dispatch(cs, count, kernelIndex: ScatterOutputsKernel);
			ComputeHelper.Dispatch(cs, count, kernelIndex: CopyBackKernel);
		}

		public void Release()
		{
			ComputeHelper.Release(sortedItemsBuffer, sortedValuesBuffer, countsBuffer);
			scan.Release();
		}
	}
}