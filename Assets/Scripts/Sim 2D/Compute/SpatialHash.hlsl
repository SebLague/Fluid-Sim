static const int2 offsets2D[9] =
{
	int2(-1, 1),
	int2(0, 1),
	int2(1, 1),
	int2(-1, 0),
	int2(0, 0),
	int2(1, 0),
	int2(-1, -1),
	int2(0, -1),
	int2(1, -1),
};

// Constants used for hashing
static const uint hashK1 = 15823;
static const uint hashK2 = 9737333;

// Convert floating point position into an integer cell coordinate
int2 GetCell2D(float2 position, float radius)
{
	return (int2)floor(position / radius);
}

// Hash cell coordinate to a single unsigned integer
uint HashCell2D(int2 cell)
{
	cell = (uint2)cell;
	uint a = cell.x * hashK1;
	uint b = cell.y * hashK2;
	return (a + b);
}

uint KeyFromHash(uint hash, uint tableSize)
{
	return hash % tableSize;
}
