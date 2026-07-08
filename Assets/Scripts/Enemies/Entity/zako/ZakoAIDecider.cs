using UnityEngine;

public class CommonEnemy : EntityBase
{
    // 如果小怪需要射击，依然可以直接挂载你的 BossBulletEmitter（记得改名为 ProjectileEmitter）
    private BossBulletEmitter emitter;
    private Transform player;

    protected override void Awake()
    {
        base.Awake();
        emitter = GetComponent<BossBulletEmitter>();
        player = GameObject.FindGameObjectWithTag("Player").transform;

        if (emitter != null) emitter.Init(player);
    }

    void Update()
    {
        if (isDead) return;

        // 小怪非常简单的 AI：距离小于 5 就一直开火 (Random 模式)
        float dist = Vector2.Distance(transform.position, player.position);
        // 假设你在 Emitter 里公开了 IsShooting 属性
        if (dist < 5f && emitter != null) // && !emitter.IsShooting) 
        {
            emitter.StartAttack(BossAttackType.Random);
        }
        else if (dist >= 5f && emitter != null)
        {
            emitter.StopAttack();
        }
    }

    // 小怪默认的死亡方式，不需要重写，直接执行基类的 Destroy(gameObject) 即可
}