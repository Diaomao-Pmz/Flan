using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 零分配对象池。
///
/// 【本次改动】
/// 1. Prewarm 从 Start() 移到 Awake()，并加 DefaultExecutionOrder 保证最早执行 ——
///    否则任何在自己 Start() 里调 Get() 的组件都可能拿到 null。
/// 2. Recycle 改为单参数 Recycle(GameObject)，key 由 PooledObject 自行携带。
/// 3. 借还路径不再调用 GetComponents（每次都会 new 数组），改用 PooledObject 的缓存。
/// 4. 重复回收被 isInPool 幂等挡住。
/// 5. key 未注册时明确报错，不再静默 return 导致对象飘在场景里泄漏。
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ObjectPoolManager : Singleton<ObjectPoolManager>
{
    [System.Serializable]
    public class PoolEntry
    {
        public string key;
        public GameObject prefab;
        public int prewarmCount = 50;
        public int maxSize = 200;
    }

    [SerializeField] private PoolEntry[] poolConfigs;

    private readonly Dictionary<string, Queue<GameObject>> pool
        = new Dictionary<string, Queue<GameObject>>();
    private readonly Dictionary<string, GameObject> prefabMap
        = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, int> maxSizeMap
        = new Dictionary<string, int>();

    protected override void Awake()
    {
        base.Awake();

        // 基类判定为重复实例时会 Destroy 自己，此时不该再建池。
        if (Instance != this) return;

        BuildPools();
    }

    private void BuildPools()
    {
        if (poolConfigs == null) return;

        foreach (PoolEntry entry in poolConfigs)
        {
            if (entry == null) continue;

            if (string.IsNullOrEmpty(entry.key))
            {
                Debug.LogError("[Pool] 存在 key 为空的池配置，已跳过。", this);
                continue;
            }
            if (entry.prefab == null)
            {
                Debug.LogError($"[Pool] 池 \"{entry.key}\" 未指定预制体，已跳过。", this);
                continue;
            }
            if (pool.ContainsKey(entry.key))
            {
                Debug.LogError($"[Pool] 池 key 重复: \"{entry.key}\"，后者已忽略。", this);
                continue;
            }

            pool[entry.key] = new Queue<GameObject>(Mathf.Max(entry.prewarmCount, 4));
            prefabMap[entry.key] = entry.prefab;
            maxSizeMap[entry.key] = Mathf.Max(entry.maxSize, 1);

            Prewarm(entry.key, entry.prewarmCount);
        }
    }

    private void Prewarm(string key, int count)
    {
        Queue<GameObject> queue = pool[key];

        for (int i = 0; i < count; i++)
        {
            GameObject obj = CreateNew(key);
            if (obj == null) return;

            obj.SetActive(false);
            obj.GetComponent<PooledObject>().SetInPool(true);
            queue.Enqueue(obj);
        }
    }

    private GameObject CreateNew(string key)
    {
        if (!prefabMap.TryGetValue(key, out GameObject prefab) || prefab == null) return null;

        GameObject obj = Instantiate(prefab, transform);
        obj.name = prefab.name;

        // 预制体不需要手动挂 PooledObject，这里统一补齐。
        // 只在「新建实例」时发生，不在借还路径上，无运行时开销。
        if (!obj.TryGetComponent(out PooledObject po))
        {
            po = obj.AddComponent<PooledObject>();
        }
        po.BindKey(key);

        return obj;
    }

    /// <summary>从池中借出一个对象。key 未注册时返回 null 并报错。</summary>
    public GameObject Get(string key)
    {
        if (!pool.TryGetValue(key, out Queue<GameObject> queue))
        {
            Debug.LogError($"[Pool] 未注册的池 key: \"{key}\"，请检查 Inspector 配置或调用方拼写。", this);
            return null;
        }

        // 队列里可能残留被外部 Destroy 掉的「空洞」，逐个跳过。
        GameObject obj = null;
        while (queue.Count > 0 && obj == null)
        {
            obj = queue.Dequeue();
        }
        if (obj == null) obj = CreateNew(key);
        if (obj == null) return null;

        PooledObject po = obj.GetComponent<PooledObject>();
        po.BindKey(key);
        po.SetInPool(false);

        obj.SetActive(true);

        IPoolable[] poolables = po.Poolables;
        for (int i = 0; i < poolables.Length; i++)
        {
            poolables[i].OnSpawn();
        }

        return obj;
    }

    public T Get<T>(string key) where T : Component
    {
        GameObject obj = Get(key);
        return obj != null ? obj.GetComponent<T>() : null;
    }

    /// <summary>
    /// 归还对象。无需传 key —— PooledObject 自己记着。
    /// 对同一对象重复调用是安全的（第二次直接返回）。
    /// </summary>
    public void Recycle(GameObject obj)
    {
        if (obj == null) return;

        if (!obj.TryGetComponent(out PooledObject po))
        {
            Debug.LogError(
                $"[Pool] \"{obj.name}\" 没有 PooledObject 组件，不是池化对象，无法回收。", obj);
            return;
        }

        // 幂等：挡住同一帧内的重复回收（例如同时触发了越界回收和命中回收）。
        if (po.IsInPool) return;

        if (!pool.TryGetValue(po.PoolKey, out Queue<GameObject> queue))
        {
            Debug.LogError(
                $"[Pool] \"{obj.name}\" 记录的 key \"{po.PoolKey}\" 未注册，无法回收。", obj);
            return;
        }

        IPoolable[] poolables = po.Poolables;
        for (int i = 0; i < poolables.Length; i++)
        {
            poolables[i].OnDespawn();
        }

        po.SetInPool(true);
        obj.SetActive(false);
        obj.transform.SetParent(transform, false);

        if (queue.Count < maxSizeMap[po.PoolKey])
        {
            queue.Enqueue(obj);
        }
        else
        {
            Destroy(obj);
        }
    }

    /// <summary>
    /// 【过渡用】旧的双参数签名。key 参数会被忽略。
    /// 编译器会给出 Obsolete 警告，把警告清干净即完成迁移，之后可删掉本重载。
    /// </summary>
    [System.Obsolete("请改用 Recycle(GameObject)，池 key 现由 PooledObject 自行记录。")]
    public void Recycle(string key, GameObject obj) => Recycle(obj);
}
