using UnityEngine;

public class Projectile : MonoBehaviour
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
    }

    public void Setup(Vector2 direction)
    {
        moveDirection = direction;
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveDirection * speed;
    }

    public bool isEnemyProjectile = false; // 在怪物的预制体面板里把这个勾上

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        // 【修改】：直接获取 PlayerState 组件，不再通过 Player 中枢
        PlayerState playerState = hitInfo.GetComponentInParent<PlayerState>();

        // 如果是玩家自己的子弹，且打到了玩家自己，无视并退出
        if (!isEnemyProjectile && playerState != null) return;

        // 如果是怪物的子弹，不能打怪物自己 (假设怪物标签是 Enemy)
        if (isEnemyProjectile && hitInfo.CompareTag("Enemy")) return;

        // 检查撞到的物体层级是否在允许销毁的 Layer 里面
        if ((destroyLayer.value & (1 << hitInfo.gameObject.layer)) != 0)
        {
            // 怪物打玩家
            if (isEnemyProjectile && playerState != null)
            {
                // 【修改】：直接调用 PlayerState 的接口进行扣血
                playerState.health.TakeDamage(damage);
            }

            // 结算完伤害后，销毁子弹
            Destroy(gameObject);
        }
    }

    void OnBecameInvisible()
    {
        Destroy(gameObject);
    }
}