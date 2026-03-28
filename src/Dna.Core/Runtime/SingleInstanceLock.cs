using System.Security.Cryptography;
using System.Text;

namespace Dna.Core.Runtime;

/// <summary>
/// 进程单实例锁（按 scope 隔离）。
/// scope 推荐使用项目根目录，防止同一项目被多个 dna 进程并发写入。
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private bool _disposed;

    private SingleInstanceLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        _name = name;
    }

    public static bool TryAcquire(string scope, out SingleInstanceLock? handle, out string mutexName)
    {
        var normalized = string.IsNullOrWhiteSpace(scope)
            ? "default"
            : Path.GetFullPath(scope).Trim().ToLowerInvariant();
        var hash = ComputeHash(normalized);
        mutexName = $"dna.instance.{hash}";

        var mutex = new Mutex(initiallyOwned: false, name: mutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 上次实例异常退出，当前实例接管锁。
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            handle = null;
            return false;
        }

        handle = new SingleInstanceLock(mutex, mutexName);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 非拥有者释放时忽略，避免影响进程退出。
        }
        _mutex.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => _name;

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
