using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralGrid : MonoBehaviour
{
    // --- Public Settings ---
    [Header("Physical Dimensions (World Units)")]
    public float width = 100.0f;  // Fixed world width (X axis)
    public float length = 100.0f; // Fixed world length (Z axis)

    [Header("Mesh Density")]
    [Range(10, 500)]
    public int resolution = 100;  // How many cells along one axis (Density)

    // Internal cache
    private Mesh mesh;
    private Vector3[] vertices;
    private Vector2[] uv;
    private int[] triangles;

    // Track current state to avoid redundant updates
    private int currentRes = -1;

    void Start()
    {
        UpdateMesh(resolution);
    }

    // --- Controller Interface ---
    public void UpdateMesh(int newResolution)
    {
        // Only regenerate if resolution changed
        if (mesh != null && currentRes == newResolution) return;

        resolution = newResolution;
        currentRes = newResolution;
        Generate();
    }

    void Generate()
    {
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "Procedural Grid";
            GetComponent<MeshFilter>().mesh = mesh;
        }
        else
        {
            mesh.Clear();
        }

        // Enable 32-bit index buffer to support >65k vertices
        mesh.indexFormat = IndexFormat.UInt32;

        int vertCount = (resolution + 1) * (resolution + 1);

        // Reallocate arrays if size changed significantly (optional optimization)
        if (vertices == null || vertices.Length != vertCount)
        {
            vertices = new Vector3[vertCount];
            uv = new Vector2[vertCount];
        }

        // 
        // Instead of x * cellSize, we calculate a step size based on total width / resolution
        float stepX = width / resolution;
        float stepZ = length / resolution;

        // Centering offsets (so (0,0,0) is the center of the grid)
        float xOffset = width * 0.5f;
        float zOffset = length * 0.5f;

        for (int i = 0, z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++, i++)
            {
                // Core logic change: Position is fraction * total size - center offset
                vertices[i] = new Vector3(x * stepX - xOffset, 0, z * stepZ - zOffset);

                // UV mapping 0..1 across the whole grid
                uv[i] = new Vector2((float)x / resolution, (float)z / resolution);
            }
        }

        int triCount = resolution * resolution * 6;
        if (triangles == null || triangles.Length != triCount)
        {
            triangles = new int[triCount];
        }

        for (int ti = 0, vi = 0, z = 0; z < resolution; z++, vi++)
        {
            for (int x = 0; x < resolution; x++, ti += 6, vi++)
            {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + resolution + 1;
                triangles[ti + 5] = vi + resolution + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}