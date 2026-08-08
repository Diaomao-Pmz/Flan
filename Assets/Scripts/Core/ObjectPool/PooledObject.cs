using UnityEngine;

/// <summary>
/// 池化对象的身份标签。由 ObjectPoolManager 在创建实例时自动挂载，
/// 你不需要手动往预制体上加。
///
/// 它解决三件事：
/// 1. 对象自己记住来自哪个池 —— 调用方回收时不必再手写 key 字符串，杜绝拼错。
/// 2. isInPool 幂等标记 —— 挡住同一帧内的重复回收（否则同一个实例会在队列里
///    躺两份，之后被两个不同调用方同时借走）。
/// 3. 缓存 IPoolable[] —— GetComponents 每次调用都会 new 一个数组，
///    在借还高频发生的弹幕场景里这是持续的 GC 压力。
/// </summary>
[DisallowMultipleComponent]
public class PooledObject : MonoBehaviour
{
    /// <summary>所属池的 key，由 ObjectPoolManager 写入。</summary>
    public string PoolKey { get; private set; }

    /// <summary>当前是否躺在池里（未被借出）。</summary>
    public bool IsInPool { get; private set; }

    private IPoolable[] cached;
    private bool cacheReady;

    /// <summary>该物体上所有 IPoolable 组件，首次访问后缓存，之后零分配。</summary>
    public IPoolable[] Poolables
    {
        get
        {
            if (!cacheReady)
            {
                cached = GetComponents<IPoolable>();
                cacheReady = true;
            }
            return cached;
        }
    }

    /// <summary>仅供 ObjectPoolManager 调用。</summary>
    public void BindKey(string key) => PoolKey = key;

    /// <summary>仅供 ObjectPoolManager 调用。</summary>
    public void SetInPool(bool value) => IsInPool = value;

    /// <summary>
    /// 运行时给物体动态增删了 IPoolable 组件时，手动调用它让缓存失效。
    /// 正常流程用不到 —— 按项目军规，组件应常驻并用开关控制，而非增删。
    /// </summary>
    public void InvalidateCache() => cacheReady = false;
}
