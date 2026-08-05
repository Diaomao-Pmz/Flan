using System.Security.Cryptography;
using UnityEngine;

public class Enemy_Projectile : MonoBehaviour, IPoolable
{
    [Header("子弹属性")]
    public float speed = 5f;
    public float lifeTime = 3f;
    public int damage = 15;
    //超出屏幕此距离回收
    public float margin = 0.5f;

    [Header("碰撞检测设置")]
    [Tooltip("勾选子弹碰到哪些图层才会销毁（比如地面、敌人）")]
    public LayerMask destroyLayer;

    private Vector2 moveDirection;
    private Rigidbody2D rb;

    public bool isControlledByFormation = false;

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
        // 如果被阵型接管了，子弹自己不施加速度，跟随父物体移动即可
        if (isControlledByFormation) return;

        rb.linearVelocity = moveDirection * speed;
    }

    private void Update()
    {
        Vector3 vp = Camera.main.WorldToViewportPoint(transform.position);

        if (vp.x < -margin || vp.x > 1f + margin ||
            vp.y < -margin || vp.y > 1f + margin)
        {
            //对象池回收
            ObjectPoolManager.Instance?.Recycle("EnemyBullet", gameObject);
        }
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

    public void OnSpawn()
    {
        
    }

    public void OnDespawn()
    {
        // 【关键】：回收进池子时，一定要把状态重置为标准子弹！
        isControlledByFormation = false;
        transform.SetParent(ObjectPoolManager.Instance.transform); // 脱离阵型父节点
        rb.linearVelocity = Vector2.zero;
        //重置大小
        transform.localScale = new Vector3(0.5f, 0.5f, 1);
    }
}