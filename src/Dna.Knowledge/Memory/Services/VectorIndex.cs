using System.Numerics;
using System.Runtime.InteropServices;

namespace Dna.Memory.Services;

/// <summary>
/// 内存向量索引 — 基于余弦相似度的暴力搜索，SIMD 加速。
/// 10K 条 × 512 维 ≈ 20MB 内存，搜索 < 10ms。
/// 超过 50K 条后考虑引入 HNSW 近似搜索（V3+）。
/// </summary>
internal class VectorIndex
{
    private readonly object _lock = new();
    private readonly Dictionary<string, float[]> _vectors = new();
    private readonly Dictionary<string, float> _norms = new();

    public int Count
    {
        get { lock (_lock) return _vectors.Count; }
    }

    /// <summary>添加或更新一个向量</summary>
    public void Upsert(string id, float[] vector)
    {
        var norm = ComputeNorm(vector);
        lock (_lock)
        {
            _vectors[id] = vector;
            _norms[id] = norm;
        }
    }

    /// <summary>批量添加</summary>
    public void UpsertBatch(IEnumerable<(string Id, float[] Vector)> items)
    {
        lock (_lock)
        {
            foreach (var (id, vector) in items)
            {
                _vectors[id] = vector;
                _norms[id] = ComputeNorm(vector);
            }
        }
    }

    /// <summary>删除一个向量</summary>
    public void Remove(string id)
    {
        lock (_lock)
        {
            _vectors.Remove(id);
            _norms.Remove(id);
        }
    }

    /// <summary>清空所有向量</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _vectors.Clear();
            _norms.Clear();
        }
    }

    /// <summary>
    /// 搜索最相似的 top-K 向量。
    /// 返回 (Id, CosineSimilarity) 列表，按相似度降序排列。
    /// </summary>
    public List<(string Id, double Score)> Search(float[] query, int topK = 10)
    {
        var queryNorm = ComputeNorm(query);
        if (queryNorm < 1e-10f)
            return [];

        List<(string Id, double Score)> scored;

        lock (_lock)
        {
            scored = new List<(string, double)>(_vectors.Count);
            foreach (var (id, vector) in _vectors)
            {
                var entryNorm = _norms[id];
                if (entryNorm < 1e-10f) continue;

                var dot = DotProductSimd(query, vector);
                var cosine = dot / (queryNorm * entryNorm);
                scored.Add((id, cosine));
            }
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Count <= topK ? scored : scored.GetRange(0, topK);
    }

    /// <summary>SIMD 加速的点积运算</summary>
    private static float DotProductSimd(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);

        if (Vector.IsHardwareAccelerated && length >= Vector<float>.Count)
        {
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a.AsSpan(0, length));
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b.AsSpan(0, length));

            var sum = Vector<float>.Zero;
            for (var i = 0; i < spanA.Length; i++)
                sum += spanA[i] * spanB[i];

            var result = Vector.Dot(sum, Vector<float>.One);

            for (var i = spanA.Length * Vector<float>.Count; i < length; i++)
                result += a[i] * b[i];

            return result;
        }

        var dot = 0f;
        for (var i = 0; i < length; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private static float ComputeNorm(float[] vector)
    {
        var sum = DotProductSimd(vector, vector);
        return MathF.Sqrt(sum);
    }
}
