using UnityEngine;
using Flandre.CombatSystem;

public class Player_HomingProjectile : MonoBehaviour
{
    [Header("子弹属性")]
    public float speed = 5f;
    public float lifeTime = 3f;
    public int damage = 15;

    [Header("碰撞检测设置")]
    [Tooltip("勾选子弹碰到哪些图层才会销毁（比如地面、敌人）")]
    public LayerMask destroyLayer;

    // ==========================================
    // 新增：追踪模块专属变量
    // ==========================================
    [Header("追踪设置 (Homing)")]
    private bool isHoming = false;
    private Transform target;
    private float homingTurnSpeed = 200f; // 转向速度 (度/秒)，越大追踪越灵敏

    private Vector2 moveDirection;
    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        Destroy(gameObject, lifeTime);
    }

    // ==========================================
    // 接口 1：普通直线射击 (地面/空中非飞行状态用)
    // ==========================================
    public void Setup(Vector2 direction)
    {
        isHoming = false; // 确保追踪关闭
        moveDirection = direction.normalized;
        UpdateRotation();
    }

    // ==========================================
    // 接口 2：【新增】追踪射击 (飞行状态特供)
    // ==========================================
    public void SetupHoming(Transform newTarget, Vector2 initialDirection, float turnSpeed = 200f)
    {
        isHoming = true;
        target = newTarget;
        homingTurnSpeed = turnSpeed;

        // 初始给一个飞出的方向 (比如先往前飞，再拐弯找Boss，视觉效果更好)
        moveDirection = initialDirection.normalized;
        UpdateRotation();
    }

    void FixedUpdate()
    {
        // 如果开启了追踪，且目标还没死/存在
        if (isHoming && target != null)
        {
            // 1. 找到当前子弹到 Boss 的绝对方向
            Vector2 directionToTarget = (target.position - transform.position).normalized;

            // 2. 平滑转向：让子弹当前的移动方向，以指定的角速度逐渐靠拢目标方向
            moveDirection = Vector3.RotateTowards(moveDirection, directionToTarget, homingTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);

            // 3. 同步更新子弹贴图的旋转
            UpdateRotation();
        }

        // 统一输送物理动力
        rb.linearVelocity = moveDirection * speed;
    }

    // 抽出一个辅助方法，用于更新贴图朝向，保持代码整洁
    private void UpdateRotation()
    {
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        EntityBase enemy = hitInfo.GetComponent<EntityBase>();
        if (enemy == null) enemy = hitInfo.GetComponentInParent<EntityBase>();

        if (enemy != null)
        {
            enemy.TakeDamage(new DamageInfo(damage, DamageType.Ranged, transform.position, gameObject));
            Destroy(gameObject);
            return;
        }

        if ((destroyLayer.value & (1 << hitInfo.gameObject.layer)) != 0)
        {
            Destroy(gameObject);
        }
    }

    void OnBecameInvisible()
    {
        Destroy(gameObject);
    }
}