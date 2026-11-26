using UnityEngine;
using System;
using Flandre.CombatSystem;

/// <summary>
/// Boss 的作战姿态。由护盾决定：
///   护盾在 → Ranged（远程为主）
///   护盾碎 → Melee （近战为主；刚碎的 stunDuration 秒内是眩晕，不出招）
/// AI 按姿态给不同类别的招式加权（见 BossAIDecider）。
/// </summary>
public enum BossCombatMode
{
    Ranged,
    Melee
}

public class BossState : MonoBehaviour
{
    [Header("--- 配置表 ---")]
    [Tooltip("血量、护盾、破盾眩晕、二阶段阈值。必填：留空会在 Console 报错，先用默认数值")]
    [SerializeField] private BossStateConfig stateConfig;

    [Header("--- 运行时数据（只读）---")]
    public EnemyHealth health = new EnemyHealth();
    public BossMechanic bossMechanic = new BossMechanic();

    private BossStateConfig activeConfig;

    /// <summary>生效中的体质配置。没拖 → 报错，用默认数值</summary>
    public BossStateConfig Config
    {
        get
        {
            if (activeConfig == null)
            {
                if (stateConfig != null) activeConfig = stateConfig;
                else
                {
                    activeConfig = ScriptableObject.CreateInstance<BossStateConfig>();
                    activeConfig.name = "StateConfig（未配置，默认数值）";
                    Debug.LogError("[BossState] 没有拖 State Config！先用默认数值。", this);
                }
            }
            return activeConfig;
        }
    }

    void Awake()
    {
        health.Init(Config.maxHP);
        bossMechanic.Init(health, Config);
    }

    /// <summary>演出期间为 true：回盾倒计时暂停（由演出导演设置）</summary>
    [System.NonSerialized] public bool timersPaused;

    // 在这个大总管里让各种计时器跑起来（回盾倒计时）
    void Update()
    {
        if (health.isDead || timersPaused) return;

        bossMechanic.Tick(Time.deltaTime);
    }
}

[Serializable]
public class EnemyHealth
{
    /// <summary>最大血量（开场从 BossStateConfig 读入）</summary>
    public int maxHP { get; private set; } = 100;
    public int currentHP = 100;
    public bool isDead = false;

    public event Action OnStatChanged;
    public event Action OnDeath;

    public void Init(int max)
    {
        maxHP = Mathf.Max(1, max);
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

[Serializable]
public class BossMechanic
{
    // 开场由 BossState 注入；注入前（极少数组件比 Boss 先初始化时）读默认数值
    private BossStateConfig config;
    private static BossStateConfig defaults;
    private BossStateConfig Cfg
    {
        get
        {
            if (config != null) return config;
            if (defaults == null) defaults = ScriptableObject.CreateInstance<BossStateConfig>();
            return defaults;
        }
    }

    public int shieldMaxHP => Cfg.shieldMaxHP;
    public int meleeShieldDamage => Cfg.meleeShieldDamage;
    public int rangedShieldDamage => Cfg.rangedShieldDamage;
    public float stunDuration => Cfg.stunDuration;
    public float shieldRecoverTime => Cfg.shieldRecoverTime;
    public float comboExtendDuration => Cfg.comboExtendDuration;
    public int phase2Threshold => Cfg.phase2Threshold;
    public float stunKnockupSpeed => Cfg.stunKnockupSpeed;

    [Header("--- 移动与环境感知 (新增) ---")]
    public bool isCornered = false;      // 是否被逼入墙角

    [Header("--- 运行时数据 (只读) ---")]
    public int shieldCurrentHP = 300;
    public bool isShieldBroken = false;
    public bool isPhase2 = false;

    [Tooltip("破盾后已经过去了多久（只读）")]
    public float shieldBrokenTimer = 0f;

    /// <summary>这个 Boss 有没有护盾。State Config 的护盾上限填 0 = 没护盾：开场就是近战为主，不眩晕、不回盾</summary>
    public bool HasShield => shieldMaxHP > 0;

    /// <summary>当前作战姿态：护盾在 = 远程为主，护盾碎（或根本没护盾）= 近战为主</summary>
    public BossCombatMode CurrentMode => isShieldBroken ? BossCombatMode.Melee : BossCombatMode.Ranged;

    /// <summary>实际回盾时间：至少要等眩晕结束</summary>
    public float EffectiveRecoverTime => Mathf.Max(shieldRecoverTime, stunDuration);

    /// <summary>回盾进度 0~1（护盾在时为 1）。供 UI 显示</summary>
    public float ShieldRecoverProgress
        => !HasShield ? 0f
           : !isShieldBroken ? 1f
           : EffectiveRecoverTime <= 0f ? 1f
           : Mathf.Clamp01(shieldBrokenTimer / EffectiveRecoverTime);

    public event Action OnShieldBroken;

    /// <summary>
    /// 每次挨打都会广播（不论有没有盾）。参数：伤害类型、挨打那一刻的作战姿态。
    /// 受击传送等"挨打反应"都订阅这个，而不是把反应写死在这里。
    /// </summary>
    public event Action<DamageType, BossCombatMode> OnHitTaken;
    public event Action OnPhase2Triggered;
    public event Action OnShieldRecovered;
    public event Action OnShieldStatChanged;

    private EnemyHealth health;

    public void Init(EnemyHealth healthModule, BossStateConfig cfg)
    {
        this.health = healthModule;
        this.config = cfg;
        // 没护盾：直接标记为「护盾已碎」= 近战为主。不发破盾通知，所以不会眩晕
        shieldCurrentHP = HasShield ? shieldMaxHP : 0;
        isShieldBroken = !HasShield;
        isPhase2 = false;
        isCornered = false;
        shieldBrokenTimer = 0f;
    }

    /// <summary>
    /// 每帧推进（由 BossState.Update 调用）。
    /// 回盾倒计时原先放在眩晕状态里 —— 但现在眩晕只占破盾期的开头一小段，
    /// 之后 Boss 会切去移动/战斗状态打近战，所以计时必须挪到这个不随状态切换的地方。
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isShieldBroken || !HasShield) return; // 没护盾的 Boss 永远不回盾

        shieldBrokenTimer += deltaTime;

        if (shieldBrokenTimer >= EffectiveRecoverTime)
        {
            RecoverShield();
        }
    }

    /// <param name="shieldDamage">这一下扣多少盾。-1 = 没填，按近战 meleeShieldDamage / 远程 rangedShieldDamage 扣</param>
    public void TakeDamage(int rawDamage, DamageType type, int shieldDamage = -1)
    {
        // 记下挨打那一刻的姿态：这一刀如果刚好打碎护盾，仍算"有盾时挨打"
        BossCombatMode modeAtHit = CurrentMode;

        if (!isShieldBroken)
        {
            // 护盾期间：招式填了破盾值就扣它；没填按近战 / 远程的默认值扣
            int damageToShield = shieldDamage >= 0
                ? shieldDamage
                : (type == DamageType.Melee) ? meleeShieldDamage : rangedShieldDamage;
            shieldCurrentHP -= damageToShield;

            OnShieldStatChanged?.Invoke();

            if (shieldCurrentHP <= 0)
            {
                BreakShield();
            }
        }
        else
        {
            health.TakeRealDamage(rawDamage);
            if (!isPhase2 && health.currentHP <= phase2Threshold)
            {
                isPhase2 = true;
                OnPhase2Triggered?.Invoke();
            }
        }

        // 最后再广播"我挨打了"。放在破盾 / 转阶段之后，
        // 这样听众（例如受击传送）能看到这一刀造成的最新状态：刚被打晕就不会再传走
        OnHitTaken?.Invoke(type, modeAtHit);
    }

    private void BreakShield()
    {
        shieldCurrentHP = 0;
        isShieldBroken = true;
        shieldBrokenTimer = 0f;
        OnShieldBroken?.Invoke();
        OnShieldStatChanged?.Invoke();
    }

    public void RecoverShield()
    {
        if (!HasShield) return;
        isShieldBroken = false;
        shieldCurrentHP = shieldMaxHP;
        shieldBrokenTimer = 0f;
        Debug.Log("[BossMechanic] 护盾已恢复！回到远程为主阶段");
        OnShieldRecovered?.Invoke();
        OnShieldStatChanged?.Invoke();
    }
}