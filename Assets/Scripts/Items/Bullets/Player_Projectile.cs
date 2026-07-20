using UnityEngine;
using Flandre.CombatSystem; // 【新增】为了使用 DamageType 触发护盾的远程独立计算

public class Player_Projectile : MonoBehaviour
{
    [Header("子弹属性")]
    public float speed = 5f;
    public float lifeTime = 3f;
    public int damage = 15;

    [Header("碰撞检测设置")]
    [Tooltip("勾选子弹碰到哪些图层才会销毁（比如地面、敌人）")]
    public LayerMask destroyLayer;

    private Vector2 moveDirection;
    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 加上生命周期销毁，防止子弹飞出屏幕外永远不销毁导致内存泄漏
        Destroy(gameObject, lifeTime);
    }

    public void Setup(Vector2 direction)
    {
        moveDirection = direction.normalized; // 确保方向向量归一化
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveDirection * speed;
    }

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        // 检测是否打到了敌人（无论是小怪还是Boss，只要有老祖宗 EntityBase 就能统管）
        EntityBase enemy = hitInfo.GetComponent<EntityBase>();

        // 防呆设计：如果碰撞体在子物体上，尝试在父物体上找
        if (enemy == null) enemy = hitInfo.GetComponentInParent<EntityBase>();

        if (enemy != null)
        {
            //调用统一的扣血接口，并传入【远程伤害】类型！
            enemy.TakeDamage(damage, DamageType.Ranged);

            // 命中敌人后销毁子弹
            Destroy(gameObject);
            return;
        }

        //如果没打中敌人，但打中了墙壁/地面（属于 destroyLayer 中勾选的层级）
        if ((destroyLayer.value & (1 << hitInfo.gameObject.layer)) != 0)
        {
            Destroy(gameObject);
        }
    }

    // 当子弹飞出摄像机视野时自动销毁，节省性能
    void OnBecameInvisible()
    {
        Destroy(gameObject);
    }
}