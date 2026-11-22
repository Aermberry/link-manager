namespace LinkMgr.Core.Registry;

/// <summary>登记文件损坏（存在但无法解析）。</summary>
public sealed class RegistryCorruptException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>登记文件读写失败（IO/权限）。</summary>
public sealed class RegistryIoException(string message, Exception? inner = null)
    : Exception(message, inner);
