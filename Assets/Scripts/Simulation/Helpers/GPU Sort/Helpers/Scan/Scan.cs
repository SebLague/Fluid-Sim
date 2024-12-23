using Seb.Helpers;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seb.GPUSorting
{
    public class Scan
    {
        const int scanKernel = 0;
        const int combineKernel = 1;

        static readonly int elementsID = Shader.PropertyToID("Elements");
        static readonly int groupSumsID = Shader.PropertyToID("GroupSums");
        static readonly int itemCountID = Shader.PropertyToID("itemCount");
        readonly ComputeShader cs;

        readonly Dictionary<int, ComputeBuffer> freeBuffers = new();

        public Scan()
        {
            cs = ComputeHelper.LoadComputeShader("ScanTest");
        }


        public void Run(ComputeBuffer elements)
        {
            // Calculate number of groups/blocks to run in shader
            // (note: 2 values processed per thread, hence count/2)
            cs.GetKernelThreadGroupSizes(scanKernel, out uint threadsPerGroup, out _, out _);
            int numGroups = Mathf.CeilToInt(elements.count / 2f / threadsPerGroup);

            if (!freeBuffers.TryGetValue(numGroups, out ComputeBuffer groupSumBuffer))
            {
                groupSumBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numGroups);
                freeBuffers.Add(numGroups, groupSumBuffer);
            }

            cs.SetBuffer(scanKernel, elementsID, elements);
            cs.SetBuffer(scanKernel, groupSumsID, groupSumBuffer);
            cs.SetInt(itemCountID, elements.count);

            // Run scan kernel
            cs.Dispatch(scanKernel, numGroups, 1, 1);

            // If more than one group, then the groups need to be adjusted by adding on all preceding groupSums to each group
            // This can be done efficiently by first calculating the scan of the groupSums
            if (numGroups > 1)
            {
                // Recursively calculate scan groupSums
                Run(groupSumBuffer);

                // Add groupSums
                cs.SetBuffer(combineKernel, elementsID, elements);
                cs.SetBuffer(combineKernel, groupSumsID, groupSumBuffer);
                cs.SetInt(itemCountID, elements.count);
                cs.Dispatch(combineKernel, numGroups, 1, 1);
            }
        }

        public void Release()
        {
            foreach (var b in freeBuffers)
            {
                ComputeHelper.Release(b.Value);
            }
        }

    }
}