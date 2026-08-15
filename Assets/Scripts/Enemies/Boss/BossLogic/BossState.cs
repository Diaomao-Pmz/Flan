using UnityEngine;
using System;
using Flandre.CombatSystem;

public class BossState : MonoBehaviour
{
    public EnemyHealth health = new EnemyHealth();
    public BossMechanic bossMechanic = new BossMechanic();

    void Awake()
    {
        health.Init();
        bossMechanic.Init(health);
    }

    void Update()
    {
        if (bossMechanic.currentTeleportTimer > 0)
        {
            bossMechanic.currentTeleportTimer -= Time.deltaTime;
        }
    }
}

[Serializable]
public class EnemyHealth
{
    public int maxHP = 100;
    public int currentHP = 100;
    public bool isDead = false;

    public event Action OnStatChanged;
    public event Action OnDeath;

    public void Init()
    {
        currentHP = maxHP;
        isDead = false;
        OnStatChanged?.Invoke();
    }

    public void TakeRealDamage(int damage)
    {
        if (isDead) return;
        currentHP = Mathf.Clamp(currentHP - damage, 0, maxHP);
        OnStatChanged?.Invoke();

        if (currentHP <= 0 && !isDead)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }
}

/// <summary>
/// Boss 的护盾 / 阶段 / 传送机制。
///
/// ==========================================================
/// 【本批改动】
///
/// 1. TakeDamage 改为接收完整的 DamageInfo，而不是拆开的 (int, DamageType)。
///    因为打断信息（breakMask）也要一路传进来，
///    每加一个字段就改一次签名的话，以后每次扩展都要动所有调用方。
///    直接传载荷本身，扩展时签名不变。
///
/// 2. 清掉了 4 条每次命中都刷屏的 Debug.Log（还带字符串拼接，
///    在弹幕互殴时是持续的 GC 压力）。保留的都改成条件编译的 PlayerLog.Info。
///
/// 3. 新增 OnInterruptRequested 事件 —— 本类只负责"广播玩家想打断"，
///    至于当前正在做的动作能不能被打断、打断后怎么表现，
///    是 BossController 的事。黑板不做决策。
/// ==========================================================
/// </summary>
[Serializable]
public class BossMechanic
{
    [Header("--- 护盾与阶段配置 ---")]
    public int shieldMaxHP = 300;
    public int meleeShieldDamage = 100;
    public int rangedShieldDamage = 3;
    public float shieldRecoverTime = 5f;
    public float comboExtendDuration = 1f;
    public int phase2Threshold = 30;
    public float stunKnockupSpeed = 15f;

    [Header("--- 移动与环境感知 ---")]
    public bool isCornered = false;
    public float teleportCooldown = 5f;
    public float currentTeleportTimer = 0f;

    [Header("--- 运行时数据 (只读) ---")]
    public int shieldCurrentHP = 300;
    public bool isShieldBroken = false;
    public bool isPhase2 = false;

    public event Action OnShieldBroken;
    public event Action OnTeleportTriggered;
    public event Action OnPhase2Triggered;
    public event Action OnShieldRecovered;
    public event Action OnShieldStatChanged;

    /// <summary>
    /// 玩家打出了带打断能力的攻击。
    /// 【本事件只是"提出请求"】—— 能不能打断由 BossController 结合
    /// 当前正在执行的动作类别来裁决。黑板不做决策。
    /// </summary>
    public event Action<DamageInfo> OnInterruptRequested;

    private EnemyHealth health;

    public void Init(EnemyHealth healthModule)
    {
        this.health = healthModule;
        shieldCurrentHP = shieldMaxHP;
        isShieldBroken = false;
        isPhase2 = false;
        isCornered = false;
        currentTeleportTimer = 0f;
    }

    public void TakeDamage(in DamageInfo info)
    {
        // 先广播打断请求 —— 无论护盾在不在，打断都该生效。
        // 顺序很重要：放在护盾结算【之前】，
        // 否则这一击刚好破盾时，会先进破防状态再收到打断请求，逻辑就绕了。
        if (info.breakMask != ActionCategory.None)
        {
            OnInterruptRequested?.Invoke(info);
        }

        if (!isShieldBroken)
        {
            int damageToShield = (info.type == DamageType.Melee)
                ? meleeShieldDamage
                : rangedShieldDamage;

            shieldCurrentHP -= damageToShield;

            // 近战击中护盾触发防反传送
            if (info.type == DamageType.Melee)
            {
                OnTeleportTriggered?.Invoke();
            }

            OnShieldStatChanged?.Invoke();

            if (shieldCurrentHP <= 0) BreakShield();
        }
        else
        {
            health.TakeRealDamage(info.amount);

            if (!isPhase2 && health.currentHP <= phase2Threshold)
            {
                isPhase2 = true;
                OnPhase2Triggered?.Invoke();
            }
        }
    }

    /// <summary>【过渡用】旧签名。新代码请直接传 DamageInfo。</summary>
    [Obsolete("请改用 TakeDamage(in DamageInfo)，以便携带打断信息。")]
    public void TakeDamage(int rawDamage, DamageType type)
    {
        TakeDamage(new DamageInfo(rawDamage, type, Vector2.zero));
    }

    private void BreakShield()
    {
        shieldCurrentHP = 0;
        isShieldBroken = true;
        OnShieldBroken?.Invoke();
        OnShieldStatChanged?.Invoke();
    }

    public void RecoverShield()
    {
        isShieldBroken = false;
        shieldCurrentHP = shieldMaxHP;
        OnShieldRecovered?.Invoke();
        OnShieldStatChanged?.Invoke();
    }
}
