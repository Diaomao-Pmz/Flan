using UnityEngine;

public class Enemy_Projectile : MonoBehaviour
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
        PlayerState playerState = hitInfo.GetComponentInParent<PlayerState>();
        if (playerState != null)
        {
            // 核心：用玩家的 X 坐标 减去 子弹的 X 坐标，算出纯正的相对方位！
            float relativeX = playerState.transform.position.x - transform.position.x;

            // 带着这个方向，把伤害传给 Health 组件
            Vector2 realKnockback = new Vector2(Mathf.Sign(relativeX) * 8f, 5f);
            playerState.health.TakeDamage(damage, realKnockback, playerState);
        }
    }

    void OnBecameInvisible()
    {
        Destroy(gameObject);
    }
}