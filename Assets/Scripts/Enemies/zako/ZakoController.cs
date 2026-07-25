using UnityEngine;

// 1. 继承自 EntityBase，小怪自动获得了 HP、TakeDamage 和 Die 的能力
public class ZakoController : EntityBase
{
    [Header("Zako AI Settings")]
    public float attackRange = 5f; // 发现玩家并开火的距离

    // 组件引用
    // 注意：如果你的 BossBulletEmitter 还没有改名，就先用 BossBulletEmitter
    private BossBulletEmitter emitter;
    private Transform player;

    private bool isAttacking = false; // 记录当前是否正在攻击

    protected override void Awake()
    {
        // 2. 极其重要：必须调用基类的 Awake，用来初始化最大血量 (currentHP = maxHP)
        base.Awake();

        // 3. 获取身上的发射器组件
        emitter = GetComponent<BossBulletEmitter>();

        // 4. 自动寻找场景里的玩家 (确保你的玩家物体上打上了 "Player" 的 Tag)
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            if (emitter != null)
            {
                emitter.Init(player);
            }
        }
    }

    void Update()
    {
        // 如果小怪已经被打死了，就立刻停止所有思考和行动
        if (isDead) return;

        // 5. 极其简单的 AI 逻辑：靠近就射击，跑远就停火
        if (player != null && emitter != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);

            if (dist <= attackRange && !isAttacking)
            {
                // 玩家进入射程，开始使用指定的弹幕攻击（比如普通小怪只会发射直线弹）
                emitter.StartAttack(BossAttackType.Line, 0f);
                isAttacking = true;
            }
            else if (dist > attackRange && isAttacking)
            {
                // 玩家跑出了射程，停止攻击
                emitter.StopAttack();
                isAttacking = false;
            }
        }
    }

    // 6. （可选）重写死亡逻辑
    // 基类 EntityBase 里的 Die() 默认是直接 Destroy(gameObject)。
    // 如果你想让小怪死的时候爆金币、播特效，可以在这里重写：
    protected override void Die()
    {
        Debug.Log("小怪被击杀了！播放死亡音效...");
        // 比如：Instantiate(explosionEffect, transform.position, Quaternion.identity);

        // 别忘了最后调用基类的 Die，把游戏物体真正销毁掉
        base.Die();
    }
}