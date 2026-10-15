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
    public EnemyHealth health = new EnemyHealth();
    public BossMechanic bossMechanic = new BossMechanic();

    void Awake()
    {
        health.Init();
        bossMechanic.Init(health);
    }

<<<<<<< Updated upstream
    // ������������������ܹ����� CD ������
=======
    // 在这个大总管里让各种计时器跑起来（回盾倒计时）
>>>>>>> Stashed changes
    void Update()
    {
        if (health.isDead) return;

        bossMechanic.Tick(Time.deltaTime);
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
    [Header("--- ������׶����� ---")]
    public int shieldMaxHP = 300;
    public int meleeShieldDamage = 100;
    public int rangedShieldDamage = 3;

    [Tooltip("破盾后的眩晕时长 n（秒）。眩晕期间不出招；结束后进入近战为主阶段")]
    public float stunDuration = 2f;

    [Tooltip("回盾时间 x（秒），从【破盾那一刻】开始算。\n" +
             "近战为主阶段的实际时长 = x - 眩晕时长。x 小于眩晕时长时按眩晕时长算（晕完立刻回盾）")]
    public float shieldRecoverTime = 5f;

    [Tooltip("【预留，暂未生效】玩家连招追加破盾时间")]
    public float comboExtendDuration = 1f;
    public int phase2Threshold = 30;
    public float stunKnockupSpeed = 15f;

<<<<<<< Updated upstream
    [Header("--- �ƶ��뻷����֪ (����) ---")]
    public bool isCornered = false;      // �Ƿ񱻱���ǽ��
    public float teleportCooldown = 5f;  // ������ȴʱ��
    public float currentTeleportTimer = 0f; // ��ȴ��ʱ�� (ֻ��)
=======
    [Header("--- 移动与环境感知 (新增) ---")]
    public bool isCornered = false;      // 是否被逼入墙角
>>>>>>> Stashed changes

    [Header("--- ����ʱ���� (ֻ��) ---")]
    public int shieldCurrentHP = 300;
    public bool isShieldBroken = false;
    public bool isPhase2 = false;

    [Tooltip("破盾后已经过去了多久（只读）")]
    public float shieldBrokenTimer = 0f;

    /// <summary>当前作战姿态：护盾在 = 远程为主，护盾碎 = 近战为主</summary>
    public BossCombatMode CurrentMode => isShieldBroken ? BossCombatMode.Melee : BossCombatMode.Ranged;

    /// <summary>实际回盾时间：至少要等眩晕结束</summary>
    public float EffectiveRecoverTime => Mathf.Max(shieldRecoverTime, stunDuration);

    /// <summary>回盾进度 0~1（护盾在时为 1）。供 UI 显示</summary>
    public float ShieldRecoverProgress
        => !isShieldBroken ? 1f
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

    public void Init(EnemyHealth healthModule)
    {
        this.health = healthModule;
        shieldCurrentHP = shieldMaxHP;
        isShieldBroken = false;
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
        if (!isShieldBroken) return;

        shieldBrokenTimer += deltaTime;

        if (shieldBrokenTimer >= EffectiveRecoverTime)
        {
            RecoverShield();
        }
    }

    public void TakeDamage(int rawDamage, DamageType type)
    {
<<<<<<< Updated upstream
        // ̽�� 1��ȷ�Ͻӿ��Ƿ���ı������ˣ��Լ����������˺����ͶԲ��ԣ�
        Debug.Log($"Boss �ɹ����յ��˺���������: {type}, ԭʼ�˺�: {rawDamage}</color>");

        if (!isShieldBroken)
        {
            // ̽�� 2������ǲ����ж�����Զ���˺������ type �� Ranged��һ��ֻ�� 3 �㣬��Ҫ�� 100 ���Ż��飡��
            int damageToShield = (type == DamageType.Melee) ? meleeShieldDamage : rangedShieldDamage;
            shieldCurrentHP -= damageToShield;

            Debug.Log($"�۳�����: {damageToShield} �㣬��ǰʣ�໤��: {shieldCurrentHP}</color>");

            if (type == DamageType.Melee)
            {
                Debug.Log("׼��������������...</color>");
                OnTeleportTriggered?.Invoke();
                Debug.Log("<color=cyan>[���Ͳ���] ���ʹ����ɹ���û�б�����</color>");
            }

=======
        // 记下挨打那一刻的姿态：这一刀如果刚好打碎护盾，仍算"有盾时挨打"
        BossCombatMode modeAtHit = CurrentMode;

        if (!isShieldBroken)
        {
            // 护盾期间：近战一刀扣 meleeShieldDamage，远程一发只扣 rangedShieldDamage
            int damageToShield = (type == DamageType.Melee) ? meleeShieldDamage : rangedShieldDamage;
            shieldCurrentHP -= damageToShield;

>>>>>>> Stashed changes
            OnShieldStatChanged?.Invoke();

            if (shieldCurrentHP <= 0)
            {
<<<<<<< Updated upstream
                Debug.Log("����ֵ���㣡׼������ BreakShield()</color>");
=======
>>>>>>> Stashed changes
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
        isShieldBroken = false;
        shieldCurrentHP = shieldMaxHP;
<<<<<<< Updated upstream
        Debug.Log("[BossMechanic] �����ѻָ���");
=======
        shieldBrokenTimer = 0f;
        Debug.Log("[BossMechanic] 护盾已恢复！回到远程为主阶段");
>>>>>>> Stashed changes
        OnShieldRecovered?.Invoke();
        OnShieldStatChanged?.Invoke();
    }
}