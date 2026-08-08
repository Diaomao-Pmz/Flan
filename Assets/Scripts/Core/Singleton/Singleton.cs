using UnityEngine;

/// <summary>
/// 泛型单例基类。
///
/// 【本次改动】Awake 由 private 改为 protected virtual。
/// 原因：Unity 的消息派发只会调用「最派生类型」上找到的那一个 Awake。
/// 基类若声明 private void Awake()，派生类一旦也声明 Awake，基类那个就被隐藏、
/// 永远不会执行 —— Instance 保持 null，整个单例静默失效。
/// 改为 virtual 后，派生类必须 override 并显式调用 base.Awake()。
/// </summary>
public class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    /// <summary>单例实例。只允许基类写入，避免外部误改。</summary>
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                $"[Singleton] 场景中存在重复的 {typeof(T).Name}，已销毁后来者。", gameObject);
            Destroy(gameObject);
            return;
        }

        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        // 关卡重载 / 关闭 Domain Reload 时，避免留下一个已销毁的静态引用。
        if (Instance == this) Instance = null;
    }
}