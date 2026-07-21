using System.Collections.Generic;
using UnityEngine;

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

    private Dictionary<string, Queue<GameObject>> pool = new Dictionary<string, Queue<GameObject>>();
    private Dictionary<string, GameObject> prefabMap = new Dictionary<string, GameObject>();
    private Dictionary<string, int> maxSizeMap = new Dictionary<string, int>();

    private void Start()
    {
        foreach (var entry in poolConfigs)
        {
            RegisterPool(entry.key, entry.prefab, entry.maxSize);
            Prewarm(entry.key, entry.prewarmCount);
        }
    }

    private void RegisterPool(string key, GameObject prefab, int maxSize)
    {
        if (!pool.ContainsKey(key))
        {
            pool[key] = new Queue<GameObject>();
            prefabMap[key] = prefab;
            maxSizeMap[key] = maxSize;
        }
    }

    private void Prewarm(string key, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject obj = CreateNew(key);
            obj.SetActive(false);
            pool[key].Enqueue(obj);
        }
    }

    private GameObject CreateNew(string key)
    {
        if (!prefabMap.ContainsKey(key)) return null;

        GameObject obj = Instantiate(prefabMap[key], transform);
        obj.name = prefabMap[key].name;
        return obj;
    }

    public GameObject Get(string key)
    {
        if (!pool.ContainsKey(key)) return null;

        GameObject obj;
        if (pool[key].Count > 0)
        {
            obj = pool[key].Dequeue();
        }
        else
        {
            obj = CreateNew(key);
        }

        if (obj == null) return null;

        obj.SetActive(true);
        var poolables = obj.GetComponents<IPoolable>();
        foreach (var p in poolables)
        {
            p.OnSpawn();
        }

        return obj;
    }

    public T Get<T>(string key) where T : Component
    {
        GameObject obj = Get(key);
        return obj != null ? obj.GetComponent<T>() : null;
    }

    public void Recycle(string key, GameObject obj)
    {
        if (obj == null) return;
        if (!pool.ContainsKey(key)) return;

        var poolables = obj.GetComponents<IPoolable>();
        foreach (var p in poolables)
        {
            p.OnDespawn();
        }

        obj.SetActive(false);
        obj.transform.SetParent(transform);

        if (pool[key].Count < maxSizeMap[key])
        {
            pool[key].Enqueue(obj);
        }
        else
        {
            Destroy(obj);
        }
    }
}
