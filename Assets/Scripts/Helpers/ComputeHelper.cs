using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System;

// This class contains some helper functions to make life a little easier working with compute shaders
// (Very work-in-progress!)
namespace Seb.Helpers
{
	public enum DepthMode
	{
		None = 0,
		Depth16 = 16,
		Depth24 = 24
	}
	
	
	public static class ComputeHelper
	{
		public const FilterMode defaultFilterMode = FilterMode.Bilinear;
		public const GraphicsFormat defaultGraphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
		static readonly uint[] argsBufferArray = new uint[5];

		public static void Dispatch(ComputeShader cs, Vector3Int numIterations, int kernelIndex = 0)
		{
			Dispatch(cs, numIterations.x, numIterations.y, numIterations.z, kernelIndex);
		}

		/// Convenience method for dispatching a compute shader.
		/// It calculates the number of thread groups based on the number of iterations needed.
		public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
			int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
			int numGroupsY = Mathf.CeilToInt(numIterationsY / (float)threadGroupSizes.y);
			int numGroupsZ = Mathf.CeilToInt(numIterationsZ / (float)threadGroupSizes.z);
			cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
		}

		public static int CalculateThreadGroupCount1D(ComputeShader cs, int numIterationsX, int kernelIndex = 0)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
			int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
			return numGroupsX;
		}


		/// Convenience method for dispatching a compute shader.
		/// It calculates the number of thread groups based on the size of the given texture.
		public static void Dispatch(ComputeShader cs, RenderTexture texture, int kernelIndex = 0)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
			Dispatch(cs, texture.width, texture.height, texture.volumeDepth, kernelIndex);
		}

		public static void Dispatch(ComputeShader cs, Texture2D texture, int kernelIndex = 0)
		{
			Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
			Dispatch(cs, texture.width, texture.height, 1, kernelIndex);
		}

		public static int GetStride<T>()
		{
			return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
		}

		public static ComputeBuffer CreateAppendBuffer<T>(int size = 1)
		{
			int stride = GetStride<T>();
			ComputeBuffer buffer = new ComputeBuffer(size, stride, ComputeBufferType.Append);
			buffer.SetCounterValue(0);
			return buffer;
		}


		public static void CreateAppendBuffer<T>(ref ComputeBuffer buffer, int count)
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(count, stride, ComputeBufferType.Append);
			}

			buffer.SetCounterValue(0);
		}

		public static bool CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(count, stride);
				return true;
			}

			return false;
		}


		public static ComputeBuffer CreateStructuredBuffer<T>(T[] data)
		{
			var buffer = new ComputeBuffer(data.Length, GetStride<T>());
			buffer.SetData(data);
			return buffer;
		}

		public static ComputeBuffer CreateStructuredBuffer<T>(List<T> data) where T : struct
		{
			var buffer = new ComputeBuffer(data.Count, GetStride<T>());
			buffer.SetData(data);

			return buffer;
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
		{
			int stride = GetStride<T>();
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != data.Count || buffer.stride != stride;
			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(data.Count, stride);
			}

			buffer.SetData(data);
			// Debug.Log(buffer.IsValid());
		}

		public static ComputeBuffer CreateStructuredBuffer<T>(int count)
		{
			return new ComputeBuffer(count, GetStride<T>());
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data)
		{
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
		}

		public static void SetBuffer(ComputeShader compute, ComputeBuffer buffer, string id, params int[] kernels)
		{
			for (int i = 0; i < kernels.Length; i++)
			{
				compute.SetBuffer(kernels[i], id, buffer);
			}
		}

		public static void SetBuffers(ComputeShader cs, int kernel, Dictionary<ComputeBuffer, string> nameLookup, params ComputeBuffer[] buffers)
		{
			foreach (ComputeBuffer buffer in buffers)
			{
				cs.SetBuffer(kernel, nameLookup[buffer], buffer);
			}
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, data, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(int length, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, length, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, int length, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			CreateStructuredBuffer<T>(ref buffer, length);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}


		/// Releases supplied buffer/s if not null
		public static void Release(params ComputeBuffer[] buffers)
		{
			for (int i = 0; i < buffers.Length; i++)
			{
				if (buffers[i] != null)
				{
					buffers[i].Release();
				}
			}
		}

		/// Releases supplied render textures/s if not null
		public static void Release(params RenderTexture[] textures)
		{
			for (int i = 0; i < textures.Length; i++)
			{
				if (textures[i] != null)
				{
					textures[i].Release();
				}
			}
		}

		public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0)
		{
			uint x, y, z;
			compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
			return new Vector3Int((int)x, (int)y, (int)z);
		}

		// ------ Texture Helpers ------

		public static RenderTexture CreateRenderTexture(RenderTexture template)
		{
			RenderTexture renderTexture = null;
			CreateRenderTexture(ref renderTexture, template);
			return renderTexture;
		}

		public static RenderTexture CreateRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
		{
			RenderTexture texture = new RenderTexture(width, height, (int)depthMode);
			texture.graphicsFormat = format;
			texture.enableRandomWrite = true;
			texture.autoGenerateMips = false;
			texture.useMipMap = useMipMaps;
			texture.Create();

			texture.name = name;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;
			return texture;
		}

		public static void CreateRenderTexture(ref RenderTexture texture, RenderTexture template)
		{
			if (texture != null)
			{
				texture.Release();
			}

			texture = new RenderTexture(template.descriptor);
			texture.enableRandomWrite = true;
			texture.Create();
		}

		public static void CreateRenderTexture(ref RenderTexture texture, int width, int height)
		{
			CreateRenderTexture(ref texture, width, height, defaultFilterMode, defaultGraphicsFormat);
		}

		public static RenderTexture CreateRenderTexture(int width, int height)
		{
			return CreateRenderTexture(width, height, defaultFilterMode, defaultGraphicsFormat);
		}


		public static bool CreateRenderTexture(ref RenderTexture texture, int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
		{
			if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.graphicsFormat != format || texture.depth != (int)depthMode || texture.useMipMap != useMipMaps)
			{
				if (texture != null)
				{
					texture.Release();
				}

				texture = CreateRenderTexture(width, height, filterMode, format, name, depthMode, useMipMaps);
				return true;
			}
			else
			{
				texture.name = name;
				texture.wrapMode = TextureWrapMode.Clamp;
				texture.filterMode = filterMode;
			}

			return false;
		}


		public static void CreateRenderTexture3D(ref RenderTexture texture, RenderTexture template)
		{
			CreateRenderTexture(ref texture, template);
		}

		public static void CreateRenderTexture3D(ref RenderTexture texture, int size, GraphicsFormat format, TextureWrapMode wrapMode = TextureWrapMode.Repeat, string name = "Untitled", bool mipmaps = false)
		{
			CreateRenderTexture3D(ref texture, size, size, size, format, wrapMode, name, mipmaps);
		}

		public static void CreateRenderTexture3D(ref RenderTexture texture, int width, int height, int depth, GraphicsFormat format, TextureWrapMode wrapMode = TextureWrapMode.Repeat, string name = "Untitled", bool mipmaps = false)
		{
			if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.volumeDepth != depth || texture.graphicsFormat != format)
			{
				//Debug.Log ("Create tex: update noise: " + updateNoise);
				if (texture != null)
				{
					texture.Release();
				}

				const int numBitsInDepthBuffer = 0;
				texture = new RenderTexture(width, height, numBitsInDepthBuffer);
				texture.graphicsFormat = format;
				texture.volumeDepth = depth;
				texture.enableRandomWrite = true;
				texture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
				texture.useMipMap = mipmaps;
				texture.autoGenerateMips = false;
				texture.Create();
			}

			texture.wrapMode = wrapMode;
			texture.filterMode = FilterMode.Bilinear;
			texture.name = name;
		}
		// ------ Instancing Helpers

		// Create args buffer for instanced indirect rendering
		public static ComputeBuffer CreateArgsBuffer(Mesh mesh, int numInstances)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;

			const int subMeshIndex = 0;
			uint[] args = new uint[numArgs];
			args[0] = (uint)mesh.GetIndexCount(subMeshIndex);
			args[1] = (uint)numInstances;
			args[2] = (uint)mesh.GetIndexStart(subMeshIndex);
			args[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
			args[4] = 0; // offset

			ComputeBuffer argsBuffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			argsBuffer.SetData(args);
			return argsBuffer;
		}

		public static void CreateArgsBuffer(ref ComputeBuffer buffer, uint[] args)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;
			if (buffer == null || buffer.stride != stride || buffer.count != numArgs || !buffer.IsValid())
			{
				buffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			}

			buffer.SetData(args);
		}

		static readonly uint[] singleInstanceRenderArgs =
		{
			0, // Index count (to be set)
			1, // instance count
			0, // submesh index
			0, // base vertex
			0, // offset
		};

		public static void CreateArgsBuffer(ref ComputeBuffer buffer, ComputeBuffer appendBuffer)
		{
			const int stride = sizeof(uint);
			if (buffer == null || buffer.stride != stride || buffer.count != singleInstanceRenderArgs.Length || !buffer.IsValid())
			{
				buffer = new ComputeBuffer(singleInstanceRenderArgs.Length, stride, ComputeBufferType.IndirectArguments);
			}

			buffer.SetData(singleInstanceRenderArgs);
			ComputeBuffer.CopyCount(appendBuffer, buffer, dstOffsetBytes: 0);
		}
		
		public static void CreateArgsBuffer(ref ComputeBuffer argsBuffer, Mesh mesh, int numInstances)
		{
			const int stride = sizeof(uint);
			const int numArgs = 5;
			const int subMeshIndex = 0;

			bool createNewBuffer = argsBuffer == null || !argsBuffer.IsValid() || argsBuffer.count != argsBufferArray.Length || argsBuffer.stride != stride;
			if (createNewBuffer)
			{
				Release(argsBuffer);
				argsBuffer = new ComputeBuffer(numArgs, stride, ComputeBufferType.IndirectArguments);
			}

			lock (argsBufferArray)
			{
				argsBufferArray[0] = (uint)mesh.GetIndexCount(subMeshIndex);
				argsBufferArray[1] = (uint)numInstances;
				argsBufferArray[2] = (uint)mesh.GetIndexStart(subMeshIndex);
				argsBufferArray[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
				argsBufferArray[4] = 0; // offset
				
				argsBuffer.SetData(argsBufferArray);
			}
		}

		// Create args buffer for instanced indirect rendering (number of instances comes from size of append buffer)
		public static ComputeBuffer CreateArgsBuffer(Mesh mesh, ComputeBuffer appendBuffer)
		{
			var buffer = CreateArgsBuffer(mesh, 0);
			ComputeBuffer.CopyCount(appendBuffer, buffer, sizeof(uint));
			return buffer;
		}

		// Read number of elements in append buffer
		public static int ReadAppendBufferLength(ComputeBuffer appendBuffer)
		{
			ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
			ComputeBuffer.CopyCount(appendBuffer, countBuffer, 0);

			int[] data = new int[1];
			countBuffer.GetData(data);
			Release(countBuffer);
			return data[0];
		}

		// ------ Set compute shader properties ------

		public static void SetTexture(ComputeShader compute, Texture texture, string name, params int[] kernels)
		{
			for (int i = 0; i < kernels.Length; i++)
			{
				compute.SetTexture(kernels[i], name, texture);
			}
		}

		// Set all values from settings object on the shader. Note, variable names must be an exact match in the shader.
		// Settings object can be any class/struct containing vectors/ints/floats/bools
		public static void SetParams(System.Object settings, ComputeShader shader, string variableNamePrefix = "", string variableNameSuffix = "")
		{
			var fields = settings.GetType().GetFields();
			foreach (var field in fields)
			{
				var fieldType = field.FieldType;
				string shaderVariableName = variableNamePrefix + field.Name + variableNameSuffix;

				if (fieldType == typeof(UnityEngine.Vector4) || fieldType == typeof(Vector3) || fieldType == typeof(Vector2))
				{
					shader.SetVector(shaderVariableName, (Vector4)field.GetValue(settings));
				}
				else if (fieldType == typeof(int))
				{
					shader.SetInt(shaderVariableName, (int)field.GetValue(settings));
				}
				else if (fieldType == typeof(float))
				{
					shader.SetFloat(shaderVariableName, (float)field.GetValue(settings));
				}
				else if (fieldType == typeof(bool))
				{
					shader.SetBool(shaderVariableName, (bool)field.GetValue(settings));
				}
				else
				{
					Debug.Log($"Type {fieldType} not implemented");
				}
			}
		}

		// ------ MISC -------


		// https://cmwdexint.com/2017/12/04/computeshader-setfloats/
		public static float[] PackFloats(params float[] values)
		{
			float[] packed = new float[values.Length * 4];
			for (int i = 0; i < values.Length; i++)
			{
				packed[i * 4] = values[i];
			}

			return values;
		}

		// Load compute shader by name (must be placed in Resources folder)
		public static void LoadComputeShader(ref ComputeShader shader, string name)
		{
			if (shader == null)
			{
				shader = LoadComputeShader(name);
			}
		}
		
		// Load compute shader by name (must be placed in Resources folder)
		public static ComputeShader LoadComputeShader(string name)
		{
			return Resources.Load<ComputeShader>(name.Split('.')[0]);
		}

		// Get data (cpu readback) from buffer
		public static T[] ReadbackData<T>(ComputeBuffer buffer)
		{
			T[] data = new T[buffer.count];
			buffer.GetData(data);
			return data;
		}
	}
}