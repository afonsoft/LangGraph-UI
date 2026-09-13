using System.Buffers.Binary;

namespace KnowledgeHub.Server.Embeddings;

/// <summary>float[] ↔ little-endian byte[] codec for BLOB persistence (SPEC-02).</summary>
public static class EmbeddingVectorCodec
{
    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), vector[i]);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return vector;
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na <= 0 || nb <= 0)
            return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
