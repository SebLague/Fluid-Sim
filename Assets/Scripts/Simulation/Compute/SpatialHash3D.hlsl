static const int3 offsets3D[27] =
{
	int3(-1, -1, -1),
	int3(0, -1, -1),
	int3(1, -1, -1),

	int3(-1, 0, -1),
	int3(0, 0, -1),
	int3(1, 0, -1),

	int3(-1, 1, -1),
	int3(0, 1, -1),
	int3(1, 1, -1),

	int3(-1, -1, 0),
	int3(0, -1, 0),
	int3(1, -1, 0),

	int3(-1, 0, 0),
	int3(0, 0, 0),
	int3(1, 0, 0),

	int3(-1, 1, 0),
	int3(0, 1, 0),
	int3(1, 1, 0),

	int3(-1, -1, 1),
	int3(0, -1, 1),
	int3(1, -1, 1),

	int3(-1, 0, 1),
	int3(0, 0, 1),
	int3(1, 0, 1),

	int3(-1, 1, 1),
	int3(0, 1, 1),
	int3(1, 1, 1)
};

// Constants used for hashing
static const uint hashK1 = 15823;
static const uint hashK2 = 9737333;
static const uint hashK3 = 440817757;

// Convert floating point position into an integer cell coordinate
int3 GetCell3D(float3 position, float radius)
{
	return (int3)floor(position / radius);
}

// Hash cell coordinate to a single unsigned integer
// TODO: investigate better hashing functions
uint HashCell3D(int3 cell)
{
	const uint blockSize = 50;
	uint3 ucell = (uint3) (cell + blockSize / 2);

	uint3 localCell = ucell % blockSize;
	uint3 blockID = ucell / blockSize;
	uint blockHash = blockID.x * hashK1 + blockID.y * hashK2 + blockID.z * hashK3;
	return localCell.x + blockSize * (localCell.y + blockSize * localCell.z) + blockHash;

}

uint KeyFromHash(uint hash, uint tableSize)
{
	return hash % tableSize;
}
