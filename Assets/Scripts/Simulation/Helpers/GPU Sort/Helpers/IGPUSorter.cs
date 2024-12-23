using UnityEngine;

namespace Seb.GPUSorting
{
	public interface IGPUSorter
	{
		void Run(ComputeBuffer keys, ComputeBuffer values);
		void Release();
	}
}