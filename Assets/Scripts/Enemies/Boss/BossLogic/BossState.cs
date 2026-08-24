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

    // 【新增】：在这个大总管里让 CD 跑起来
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

    [Header("--- 移动与环境感知 (新增) ---")]
    public bool isCornered = false;      // 是否被逼入墙角
    public float teleportCooldown = 5f;  // 传送冷却时间
    public float currentTeleportTimer = 0f; // 冷却计时器 (只读)

    [Header("--- 运行时数据 (只读) ---")]
    public int shieldCurrentHP = 300;
    public bool isShieldBroken = false;
    public bool isPhase2 = false;

    public event Action OnShieldBroken;
    public event Action OnTeleportTriggered;
    public event Action OnPhase2Triggered;
    public event Action OnShieldRecovered;
    public event Action OnShieldStatChanged;

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

    public void TakeDamage(int rawDamage, DamageType type)
    {
        // 探针 1：确认接口是否真的被调用了，以及传进来的伤害类型对不对！
        Debug.Log($"Boss 成功接收到伤害请求！类型: {type}, 原始伤害: {rawDamage}</color>");

        if (!isShieldBroken)
        {
            // 探针 2：检查是不是判定成了远程伤害（如果 type 是 Ranged，一次只扣 3 点，你要砍 100 刀才会碎！）
            int damageToShield = (type == DamageType.Melee) ? meleeShieldDamage : rangedShieldDamage;
            shieldCurrentHP -= damageToShield;

            Debug.Log($"扣除护盾: {damageToShield} 点，当前剩余护盾: {shieldCurrentHP}</color>");

            if (type == DamageType.Melee)
            {
                Debug.Log("准备触发防反传送...</color>");
                OnTeleportTriggered?.Invoke();
                Debug.Log("<color=cyan>[传送测试] 传送触发成功，没有报错！</color>");
            }

            OnShieldStatChanged?.Invoke();

            if (shieldCurrentHP <= 0)
            {
                Debug.Log("护盾值归零！准备触发 BreakShield()</color>");
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
        Debug.Log("[BossMechanic] 护盾已恢复！");
        OnShieldRecovered?.Invoke();
        OnShieldStatChanged?.Invoke();
    }
}