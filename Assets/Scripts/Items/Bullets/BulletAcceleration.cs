using UnityEngine;

/// <summary>
/// 子弹加速器。**常驻在子弹预制体上**，靠开关控制是否生效。
///
/// 【为什么不再用 AddComponent / Destroy】
/// 1. 违反零分配：AddComponent 与 Destroy(Component) 都是 native 侧的结构性操作，
///    会分配托管包装对象并重排组件数组。Rotation 形态 0.05 秒一发，等于每秒 20 次装拆。
/// 2. 存在「脏子弹窗口」：Destroy(Component) 延迟到帧末执行。子弹回收后若在同一帧
///    被重新借出，拿到的就是一颗还挂着旧加速器、且该组件即将消失的脏子弹 ——
///    表现为「偶尔有颗子弹速度不对」，且完全无法复现。
/// 3. 越权：原先由 Enemy_Projectile 负责清理别人的组件。现在实现 IPoolable，
///    由 ObjectPoolManager 在回收时自动调用 OnDespawn，各管各的。
///
/// 【关键改动】baseSpeed 的快照从 Start() 移到 Configure()。
/// Start() 在一个物体的生命周期里只执行一次 —— 常驻化之后，第二次借出时它不会再跑，
/// baseSpeed 会永远停在第一发子弹的速度上。快照必须在每次启用时重拍。
/// </summary>
[RequireComponent(typeof(Enemy_Projectile))]
public class BulletAcceleration : MonoBehaviour, IPoolable
{
    private Enemy_Projectile proj;

    private float accelerationRate;
    private float coef;
    private float baseSpeed;
    private float timer;

    // 未经 Configure 点亮时保持沉默，等同于「组件不存在」
    private bool isActive;

    private void Awake()
    {
        proj = GetComponent<Enemy_Projectile>();
    }

    /// <summary>
    /// 启用加速并拍下速度快照。
    /// 调用时机必须晚于 Enemy_Projectile.speed 的赋值，否则快照会取到上一发的速度。
    /// </summary>
    public void Configure(float rate, float coefficient)
    {
        if (proj == null) return;

        accelerationRate = rate;
        coef = coefficient;
        baseSpeed = proj.speed;   // 数据快照：每次启用都重拍
        timer = 0f;
        isActive = true;
    }

    /// <summary>手动关闭加速（例如中途被阵型接管）。</summary>
    public void Disable() => isActive = false;

    public void OnSpawn()
    {
        // 默认关闭。需要加速的招式会紧接着调用 Configure() 点亮。
    }

    public void OnDespawn()
    {
        // 彻底重置，绝不把脏数据带回池子
        isActive = false;
        accelerationRate = 0f;
        coef = 0f;
        baseSpeed = 0f;
        timer = 0f;
    }

    private void Update()
    {
        if (!isActive || proj == null) return;

        timer += Time.deltaTime;
        proj.speed = coef * baseSpeed + (accelerationRate * timer * timer);
    }
}