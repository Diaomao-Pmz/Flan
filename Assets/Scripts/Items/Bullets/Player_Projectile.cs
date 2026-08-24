using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 玩家子弹。已接入对象池。
///
/// ==========================================================
/// 【接池改造 · 三处致命问题】
///
/// ① Awake 里的 Destroy(gameObject, lifeTime) —— 定时炸弹
///    Awake 一辈子只跑一次（池化对象是复用的，第二次借出不会再跑）。
///    所以：射出 → 3秒倒计时启动 → 撞墙回池 → 倒计时还在走
///          → 3秒到，【躺在池里的对象被 Destroy 了】
///    池的队列里就留下一个"空洞"，越用越小，最后每次射击都要 CreateNew。
///
///    比喻：图书馆的书借出去时贴了张"三天后自动销毁"的贴纸。
///          书还回来了，贴纸还在，三天后书在书架上自燃。
///
///    改法：寿命改成每次借出时重置的计时器。
///
/// ② OnBecameInvisible() —— 回池时会误触发
///    对象回池时 SetActive(false)，渲染器变不可见，
///    这个回调会在【已经回池的对象】上再触发一次回收。
///    虽然 PooledObject.isInPool 挡得住，但逻辑上是错的。
///    改用 Enemy_Projectile 同款的 ScreenBounds.IsOutside 主动判定。
///
/// ③ Destroy(gameObject) → Recycle(gameObject)
///    命中与撞墙两条路径都要改，漏一条池就会被抽干。
///
/// 【OnDespawn 必须把状态擦干净】
/// 这是 readme 那条「绝不把脏数据带回池子」的直接落实：
/// 速度、角速度、朝向、可能被 buff 改写的 speed，全部还原到出厂快照。
/// ==========================================================
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Player_Projectile : MonoBehaviour, IPoolable
{
    [Header("子弹属性")]
    public float speed = 5f;

    [Tooltip("存活上限（秒）。每次从池里借出时重新计时")]
    public float lifeTime = 3f;

    public int damage = 15;

    [Tooltip("超出屏幕多远才回收。单位是「屏幕比例」，与敌方子弹语义一致")]
    public float margin = 0.2f;

    [Header("碰撞检测设置")]
    [Tooltip("子弹碰到哪些图层会被回收（比如地面、墙壁）")]
    public LayerMask destroyLayer;

    [Tooltip("命中敌人后是否回收。取消勾选 = 穿透弹")]
    public bool recycleOnHit = true;

    private Vector2 moveDirection;
    private Rigidbody2D rb;

    // 出厂快照。避免把被外部改写过的值带回池子
    private Vector3 originalScale;
    private float originalSpeed;
    private int originalDamage;

    // 每次借出重置的存活计时器（取代原先 Awake 里的 Destroy 定时器）
    private float aliveTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        originalScale = transform.localScale;
        originalSpeed = speed;
        originalDamage = damage;
    }

    /// <summary>
    /// 计时器在 OnEnable 里也重置一次。
    ///
    /// 为什么两处都写：从池里借出走 OnSpawn，
    /// 但如果有人在编辑器里直接 Instantiate 这个预制体做测试，
    /// OnSpawn 不会被调用 —— OnEnable 保证两种路径下都是干净的。
    /// </summary>
    void OnEnable()
    {
        aliveTimer = 0f;
    }

    /// <summary>
    /// 【本批新增】按招式配置调整这一发的表现。必须在 Setup 之前调用。
    ///
    /// 这三项都会在回池时由 OnDespawn 还原成出厂快照 ——
    /// 所以同一个池里的子弹既能当 B1 的普通弹，
    /// 也能当 BB2 的"更大更快"弹，不会把脏数据带回池子。
    /// </summary>
    /// <param name="speedMultiplier">速度倍率，1 = 原始速度</param>
    /// <param name="scaleMultiplier">体积倍率，1 = 原始大小</param>
    /// <param name="damageOverride">伤害覆盖，0 或负数 = 用预制体上的原始伤害</param>
    public void Configure(float speedMultiplier, float scaleMultiplier, int damageOverride)
    {
        if (speedMultiplier > 0f) speed = originalSpeed * speedMultiplier;

        if (scaleMultiplier > 0f && !Mathf.Approximately(scaleMultiplier, 1f))
        {
            transform.localScale = originalScale * scaleMultiplier;
        }

        if (damageOverride > 0) damage = damageOverride;
    }

    public void Setup(Vector2 direction)
    {
        moveDirection = direction.normalized;
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveDirection * speed;
    }

    void Update()
    {
        // ---- 寿命到期 ----
        aliveTimer += Time.deltaTime;
        if (lifeTime > 0f && aliveTimer >= lifeTime)
        {
            Recycle();
            return;
        }

        // ---- 飞出屏幕 ----
        // 用主动判定而不是 OnBecameInvisible：后者在对象回池被 SetActive(false) 时
        // 也会触发，等于对已经躺回池里的对象再回收一次。
        if (ScreenBounds.IsOutside(transform.position, margin))
        {
            Recycle();
        }
    }

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        // GetComponentInParent 本身也会检查自己，所以一次调用就够
        // （判定框常挂在子物体上，只用 GetComponent 会打中但不掉血）
        EntityBase enemy = hitInfo.GetComponentInParent<EntityBase>();

        if (enemy != null)
        {
            enemy.TakeDamage(new DamageInfo(
                damage, DamageType.Ranged, transform.position, gameObject));

            if (recycleOnHit) Recycle();
            return;
        }

        // 打中墙壁/地面
        if ((destroyLayer.value & (1 << hitInfo.gameObject.layer)) != 0)
        {
            Recycle();
        }
    }

    /// <summary>
    /// 统一回收出口。
    /// 单参回收：key 由 PooledObject 自己记录，不必手写字符串。
    /// 对同一对象重复调用是安全的（池内部有 isInPool 幂等挡板）。
    /// </summary>
    private void Recycle()
    {
        ObjectPoolManager.Instance?.Recycle(gameObject);
    }

    // ==========================================================
    // IPoolable
    // ==========================================================

    public void OnSpawn()
    {
        aliveTimer = 0f;
    }

    public void OnDespawn()
    {
        // 【绝不把脏数据带回池子】
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        transform.localScale = originalScale;
        transform.rotation = Quaternion.identity;

        speed = originalSpeed;      // 可能被 buff / 加速器 / 招式倍率改写过
        damage = originalDamage;
        moveDirection = Vector2.zero;
        aliveTimer = 0f;

        // 脱离父节点由 ObjectPoolManager.Recycle 统一处理，这里不重复 SetParent
    }
}
