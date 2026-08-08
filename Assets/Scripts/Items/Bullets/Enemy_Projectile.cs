using UnityEngine;
using Flandre.CombatSystem;

public class Enemy_Projectile : MonoBehaviour, IPoolable
{
    [Header("子弹属性")]
    public float speed = 5f;
    public float lifeTime = 3f;
    public int damage = 15;
    [Tooltip("超出屏幕多远才回收。单位是「屏幕比例」：0.5 表示上下左右各外扩半个屏幕，" +
             "给绕圈的阵型子弹留出甩出画面再荡回来的余地。")]
    public float margin = 0.5f;

    [Header("碰撞检测设置")]
    [Tooltip("勾选子弹碰到哪些图层才会销毁（比如地面、敌人）")]
    public LayerMask destroyLayer;

    private Vector2 moveDirection;
    private Rigidbody2D rb;

    public bool isControlledByFormation = false;
    public bool isEnemyProjectile = false; // 在怪物的预制体面板里把这个勾上

    // 出厂快照。原先 OnDespawn 里硬编码 new Vector3(0.5f, 0.5f, 1)，
    // 是从预制体抄来的魔法数字，改了预制体这里就会悄悄失配。
    private Vector3 originalScale;
    private float originalSpeed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        originalScale = transform.localScale;
        originalSpeed = speed;
    }

    public void Setup(Vector2 direction)
    {
        moveDirection = direction;
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void FixedUpdate()
    {
        // 如果被阵型接管了，子弹自己不施加速度，跟随父物体移动即可
        if (isControlledByFormation) return;

        rb.linearVelocity = moveDirection * speed;
    }

    private void Update()
    {
        // 相机边界由 ScreenBounds 每帧统一计算一次，这里只做 4 次浮点比较。
        // margin 的语义（屏幕比例）保持不变，Inspector 数值无需调整。
        if (ScreenBounds.IsOutside(transform.position, margin))
        {
            // 单参回收：key 由 PooledObject 自己记录，不再手写字符串。
            ObjectPoolManager.Instance?.Recycle(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        // 友军免伤：从已废弃的 FormationBullet 并入的唯一有价值逻辑。
        if (isEnemyProjectile && hitInfo.CompareTag("Enemy")) return;

        // 【③ 改动】仍然显式查找 PlayerState 而非泛化的 IDamageable。
        // 原因：本类是敌方子弹，若改成通用查找，一旦某个预制体忘了勾
        // isEnemyProjectile，Boss 就会被自己的弹幕打死。
        // 统一的是「载荷契约」，命中策略保持显式，零回归风险。
        PlayerState playerState = hitInfo.GetComponentInParent<PlayerState>();
        if (playerState == null) return;

        // 方向与力度全交给受击方决定。
        IDamageable target = playerState;
        target.TakeDamage(new DamageInfo(
            damage,
            DamageType.Ranged,
            transform.position,
            gameObject));

        // 注意：这里保持原有行为 —— 命中后子弹继续飞（穿透弹）。
        // 若想改成命中即回收，在此处调 ObjectPoolManager.Instance?.Recycle(gameObject);
        // 但那样需要额外记录「已命中目标」以防重复扣血。
    }

    public void OnSpawn()
    {
    }

    public void OnDespawn()
    {
        // 回收进池子时，把状态重置为标准子弹
        isControlledByFormation = false;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // 还原缩放、朝向与速度，避免脏数据带回池子。
        // speed 会被 BulletAcceleration 持续改写，必须还原。
        transform.localScale = originalScale;
        transform.rotation = Quaternion.identity;
        speed = originalSpeed;

        // 脱离父节点交由 ObjectPoolManager.Recycle 统一处理，这里不再重复 SetParent。
        // 加速器现在常驻预制体、自行实现 IPoolable，由 ObjectPoolManager 在回收时统一调用它的 OnDespawn，无需本类插手。
    }
}