#pragma kernel ExternalForces
#pragma kernel UpdateSpatialHash
#pragma kernel CalculateDensities
#pragma kernel CalculatePressureForce
#pragma kernel CalculateViscosity
#pragma kernel UpdatePositions

// Includes
#include "./FluidMaths2D.hlsl"
#include "./SpatialHash.hlsl"

static const int NumThreads = 64;

// Buffers
RWStructuredBuffer<float2> Positions;
RWStructuredBuffer<float2> PredictedPositions;
RWStructuredBuffer<float2> Velocities;
RWStructuredBuffer<float2> Densities; // Density, Near Density
RWStructuredBuffer<uint3> SpatialIndices; // used for spatial hashing
RWStructuredBuffer<uint> SpatialOffsets; // used for spatial hashing

// Settings
const uint numParticles;
const float gravity;
const float deltaTime;
const float collisionDamping;
const float smoothingRadius;
const float targetDensity;
const float pressureMultiplier;
const float nearPressureMultiplier;
const float viscosityStrength;
const float2 boundsSize;
const float2 interactionInputPoint;
const float interactionInputStrength;
const float interactionInputRadius;

const float2 obstacleSize;
const float2 obstacleCentre;

float DensityKernel(float dst, float radius)
{
	return SpikyKernelPow2(dst, radius);
}

float NearDensityKernel(float dst, float radius)
{
	return SpikyKernelPow3(dst, radius);
}

float DensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow2(dst, radius);
}

float NearDensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow3(dst, radius);
}

float ViscosityKernel(float dst, float radius)
{
	return SmoothingKernelPoly6(dst, smoothingRadius);
}

float2 CalculateDensity(float2 pos)
{
	int2 originCell = GetCell2D(pos, smoothingRadius);
	float sqrRadius = smoothingRadius * smoothingRadius;
	float density = 0;
	float nearDensity = 0;

	// Neighbour search
	for (int i = 0; i < 9; i++)
	{
		uint hash = HashCell2D(originCell + offsets2D[i]);
		uint key = KeyFromHash(hash, numParticles);
		uint currIndex = SpatialOffsets[key];

		while (currIndex < numParticles)
		{
			uint3 indexData = SpatialIndices[currIndex];
			currIndex++;
			// Exit if no longer looking at correct bin
			if (indexData[2] != key) break;
			// Skip if hash does not match
			if (indexData[1] != hash) continue;

			uint neighbourIndex = indexData[0];
			float2 neighbourPos = PredictedPositions[neighbourIndex];
			float2 offsetToNeighbour = neighbourPos - pos;
			float sqrDstToNeighbour = dot(offsetToNeighbour, offsetToNeighbour);

			// Skip if not within radius
			if (sqrDstToNeighbour > sqrRadius) continue;

			// Calculate density and near density
			float dst = sqrt(sqrDstToNeighbour);
			density += DensityKernel(dst, smoothingRadius);
			nearDensity += NearDensityKernel(dst, smoothingRadius);
		}
	}

	return float2(density, nearDensity);
}

float PressureFromDensity(float density)
{
	return (density - targetDensity) * pressureMultiplier;
}

float NearPressureFromDensity(float nearDensity)
{
	return nearPressureMultiplier * nearDensity;
}

float2 ExternalForces(float2 pos, float2 velocity)
{
	// Gravity
	float2 gravityAccel = float2(0, gravity);
	
	// Input interactions modify gravity
	if (interactionInputStrength != 0) {
		float2 inputPointOffset = interactionInputPoint - pos;
		float sqrDst = dot(inputPointOffset, inputPointOffset);
		if (sqrDst < interactionInputRadius * interactionInputRadius)
		{
			float dst = sqrt(sqrDst);
			float edgeT = (dst / interactionInputRadius);
			float centreT = 1 - edgeT;
			float2 dirToCentre = inputPointOffset / dst;

			float gravityWeight = 1 - (centreT * saturate(interactionInputStrength / 10));
			float2 accel = gravityAccel * gravityWeight + dirToCentre * centreT * interactionInputStrength;
			accel -= velocity * centreT;
			return accel;
		}
	}

	return gravityAccel;
}


void HandleCollisions(uint particleIndex)
{
	float2 pos = Positions[particleIndex];
	float2 vel = Velocities[particleIndex];

	// Keep particle inside bounds
	const float2 halfSize = boundsSize * 0.5;
	float2 edgeDst = halfSize - abs(pos);

	if (edgeDst.x <= 0)
	{
		pos.x = halfSize.x * sign(pos.x);
		vel.x *= -1 * collisionDamping;
	}
	if (edgeDst.y <= 0)
	{
		pos.y = halfSize.y * sign(pos.y);
		vel.y *= -1 * collisionDamping;
	}

	// Collide particle against the test obstacle
	const float2 obstacleHalfSize = obstacleSize * 0.5;
	float2 obstacleEdgeDst = obstacleHalfSize - abs(pos - obstacleCentre);

	if (obstacleEdgeDst.x >= 0 && obstacleEdgeDst.y >= 0)
	{
		if (obstacleEdgeDst.x < obstacleEdgeDst.y) {
			pos.x = obstacleHalfSize.x * sign(pos.x - obstacleCentre.x) + obstacleCentre.x;
			vel.x *= -1 * collisionDamping;
		}
		else {
			pos.y = obstacleHalfSize.y * sign(pos.y - obstacleCentre.y) + obstacleCentre.y;
			vel.y *= -1 * collisionDamping;
		}
	}

	// Update position and velocity
	Positions[particleIndex] = pos;
	Velocities[particleIndex] = vel;
}

[numthreads(NumThreads,1,1)]
void ExternalForces(uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;

	// External forces (gravity and input interaction)
	Velocities[id.x] += ExternalForces(Positions[id.x], Velocities[id.x]) * deltaTime;

	// Predict
	const float predictionFactor = 1 / 120.0;
	PredictedPositions[id.x] = Positions[id.x] + Velocities[id.x] * predictionFactor;
}

[numthreads(NumThreads,1,1)]
void UpdateSpatialHash (uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;

	// Reset offsets
	SpatialOffsets[id.x] = numParticles;
	// Update index buffer
	uint index = id.x;
	int2 cell = GetCell2D(PredictedPositions[index], smoothingRadius);
	uint hash = HashCell2D(cell);
	uint key = KeyFromHash(hash, numParticles);
	SpatialIndices[id.x] = uint3(index, hash, key);
}

[numthreads(NumThreads,1,1)]
void CalculateDensities (uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;

	float2 pos = PredictedPositions[id.x];
	Densities[id.x] = CalculateDensity(pos);
}

[numthreads(NumThreads,1,1)]
void CalculatePressureForce (uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;

	float density = Densities[id.x][0];
	float densityNear = Densities[id.x][1];
	float pressure = PressureFromDensity(density);
	float nearPressure = NearPressureFromDensity(densityNear);
	float2 pressureForce = 0;
	
	float2 pos = PredictedPositions[id.x];
	int2 originCell = GetCell2D(pos, smoothingRadius);
	float sqrRadius = smoothingRadius * smoothingRadius;

	// Neighbour search
	for (int i = 0; i < 9; i ++)
	{
		uint hash = HashCell2D(originCell + offsets2D[i]);
		uint key = KeyFromHash(hash, numParticles);
		uint currIndex = SpatialOffsets[key];

		while (currIndex < numParticles)
		{
			uint3 indexData = SpatialIndices[currIndex];
			currIndex ++;
			// Exit if no longer looking at correct bin
			if (indexData[2] != key) break;
			// Skip if hash does not match
			if (indexData[1] != hash) continue;

			uint neighbourIndex = indexData[0];
			// Skip if looking at self
			if (neighbourIndex == id.x) continue;

			float2 neighbourPos = PredictedPositions[neighbourIndex];
			float2 offsetToNeighbour = neighbourPos - pos;
			float sqrDstToNeighbour = dot(offsetToNeighbour, offsetToNeighbour);

			// Skip if not within radius
			if (sqrDstToNeighbour > sqrRadius) continue;

			// Calculate pressure force
			float dst = sqrt(sqrDstToNeighbour);
			float2 dirToNeighbour = dst > 0 ? offsetToNeighbour / dst : float2(0, 1);

			float neighbourDensity = Densities[neighbourIndex][0];
			float neighbourNearDensity = Densities[neighbourIndex][1];
			float neighbourPressure = PressureFromDensity(neighbourDensity);
			float neighbourNearPressure = NearPressureFromDensity(neighbourNearDensity);

			float sharedPressure = (pressure + neighbourPressure) * 0.5;
			float sharedNearPressure = (nearPressure + neighbourNearPressure) * 0.5;

			pressureForce += dirToNeighbour * DensityDerivative(dst, smoothingRadius) * sharedPressure / neighbourDensity;
			pressureForce += dirToNeighbour * NearDensityDerivative(dst, smoothingRadius) * sharedNearPressure / neighbourNearDensity;
		}
	}

	float2 acceleration = pressureForce / density;
	Velocities[id.x] += acceleration * deltaTime;//
}



[numthreads(NumThreads,1,1)]
void CalculateViscosity (uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;
	
		
	float2 pos = PredictedPositions[id.x];
	int2 originCell = GetCell2D(pos, smoothingRadius);
	float sqrRadius = smoothingRadius * smoothingRadius;

	float2 viscosityForce = 0;
	float2 velocity = Velocities[id.x];

	for (int i = 0; i < 9; i ++)
	{
		uint hash = HashCell2D(originCell + offsets2D[i]);
		uint key = KeyFromHash(hash, numParticles);
		uint currIndex = SpatialOffsets[key];

		while (currIndex < numParticles)
		{
			uint3 indexData = SpatialIndices[currIndex];
			currIndex ++;
			// Exit if no longer looking at correct bin
			if (indexData[2] != key) break;
			// Skip if hash does not match
			if (indexData[1] != hash) continue;

			uint neighbourIndex = indexData[0];
			// Skip if looking at self
			if (neighbourIndex == id.x) continue;

			float2 neighbourPos = PredictedPositions[neighbourIndex];
			float2 offsetToNeighbour = neighbourPos - pos;
			float sqrDstToNeighbour = dot(offsetToNeighbour, offsetToNeighbour);

			// Skip if not within radius
			if (sqrDstToNeighbour > sqrRadius) continue;

			float dst = sqrt(sqrDstToNeighbour);
			float2 neighbourVelocity = Velocities[neighbourIndex];
			viscosityForce += (neighbourVelocity - velocity) * ViscosityKernel(dst, smoothingRadius);
		}

	}
	Velocities[id.x] += viscosityForce * viscosityStrength * deltaTime;
}

[numthreads(NumThreads, 1, 1)]
void UpdatePositions(uint3 id : SV_DispatchThreadID)
{
	if (id.x >= numParticles) return;

	Positions[id.x] += Velocities[id.x] * deltaTime;
	HandleCollisions(id.x);
}