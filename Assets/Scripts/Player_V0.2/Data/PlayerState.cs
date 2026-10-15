using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【玩家数据总线 / 黑板】—— 对标敌人侧的 BossState。
///
/// 【批次B 新增】穿透图层的引用计数。
///
/// 原先 gameObject.layer 被 Relay 的跳/冲/铲三处各自直接赋值：
///     sm.gameObject.layer = invincibleLayer;   // Enter
///     sm.gameObject.layer = playerLayer;       // Exit
///
/// 只要两个 Relay 动作有一点时序交错（比如冲刺无敌中触发滑铲），
/// 先结束的那个就会把还在进行中的那个的图层一起还原。
///
/// 比喻：一间房里三个人共用一盏灯的开关。
///       A 开灯干活，B 也开灯干活，A 干完随手关灯 —— B 还在黑暗里。
///
/// 改成引用计数：每人一把钥匙，全部还回来才熄灯。
/// </summary>
public class PlayerState : MonoBehaviour, IDamageable
{
    [Header("出厂说明书 (ScriptableObject · 运行时只读)")]
    [Tooltip("资源类出厂值：血、蓝、回蓝、耗蓝")]
    public PlayerStateConfig stateConfig;
    [Tooltip("手感：跑、跳、冲刺、滑铲、飞行速度")]
    public PlayerMovementConfig movementConfig;
    [Tooltip("受击规则：硬直、击飞、闪烁、无敌时长")]
    public PlayerCombatConfig combatConfig;

    [Header("成长表 (可选 · 不填则没有成长加成)")]
    [Tooltip("每个属性升一级加多少、最多几级。技能树通过 AddAttributeLevel 使用它")]
    public PlayerAttributeConfig attributeConfig;

    [Header("图层设置")]
    [Tooltip("玩家常态所在图层名")]
    public string playerLayerName = "Player";
    [Tooltip("可穿透敌人时切换到的图层名")]
    public string phaseThroughLayerName = "Invincible";

    private int playerLayer = -1;
    private int phaseThroughLayer = -1;

    [HideInInspector] public PlayerStats stats = new PlayerStats();
    [HideInInspector] public PlayerHealth health = new PlayerHealth();

    // 穿透图层的引用计数
    private readonly HashSet<object> phaseThroughSources = new HashSet<object>();

    private bool isInitialized = false;

    // ---- 成长等级（运行时 / 存档数据）----
    private readonly Dictionary<PlayerAttribute, int> attributeLevels = new Dictionary<PlayerAttribute, int>();

    /// <summary>
    /// 成长加成的便利贴签名。所有成长加成都用这一个签名 ——
    /// 每个属性是一面独立的墙，撕"这面墙上成长贴的"不会碰到别的墙，也不会碰到宝石贴的。
    /// </summary>
    private static readonly object AttributeGrowthSource = new object();

    /// <summary>某个属性的成长等级变化时广播：(属性, 新等级)。技能树 UI 可以订阅它</summary>
    public event System.Action<PlayerAttribute, int> OnAttributeLevelChanged;

    void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// 幂等初始化。
    /// Unity 不保证同一物体上各组件 Awake 的顺序，
    /// 所以 PlayerStateMachine 也会调用它一次，谁先醒都不会拿到空数据。
    /// </summary>
    public void EnsureInitialized()
    {
        if (isInitialized) return;
        isInitialized = true;

        if (movementConfig == null)
        {
            movementConfig = ScriptableObject.CreateInstance<PlayerMovementConfig>();
            Debug.LogWarning(
                "[PlayerState] 未指定 PlayerMovementConfig，已临时创建默认配置。\n" +
                "请右键 Create > Flandre > Player > Movement Config 生成资产并拖上来。" +
                "（临时配置不会保存）", this);
        }

        if (stateConfig == null)
        {
            stateConfig = ScriptableObject.CreateInstance<PlayerStateConfig>();
            Debug.LogWarning(
                "[PlayerState] 未指定 PlayerStateConfig，已临时创建默认配置。\n" +
                "请把 Config 文件夹里的 PlayerStateConfig 资产拖上来。" +
                "（临时配置不会保存）", this);
        }

        if (combatConfig == null)
        {
            combatConfig = ScriptableObject.CreateInstance<PlayerCombatConfig>();
            Debug.LogWarning(
                "[PlayerState] 未指定 PlayerCombatConfig，已临时创建默认配置。\n" +
                "请右键 Create > Flandre > Player > Combat Config 生成资产并拖上来。" +
                "（临时配置不会保存）", this);
        }

        // 顺序：先 Stats（算出最大值）再 Health（按最大值回满）
        stats.Init(movementConfig, stateConfig);
        health.Init(combatConfig, stats);

        // 图层解析。找不到就退化为「不切图层」，而不是崩溃或切到 layer 0
        playerLayer = LayerMask.NameToLayer(playerLayerName);
        phaseThroughLayer = LayerMask.NameToLayer(phaseThroughLayerName);

        if (playerLayer < 0)
            Debug.LogWarning($"[PlayerState] 找不到图层「{playerLayerName}」，穿透功能已禁用。", this);
        if (phaseThroughLayer < 0)
            Debug.LogWarning($"[PlayerState] 找不到图层「{phaseThroughLayerName}」，穿透功能已禁用。", this);
    }

    // ==========================================
    // 成长：等级 API
    // ==========================================

    /// <summary>某个属性当前的成长等级（没升过为 0）</summary>
    public int GetAttributeLevel(PlayerAttribute attribute)
        => attributeLevels.TryGetValue(attribute, out int lv) ? lv : 0;

    /// <summary>
    /// 把某个属性的成长等级设为指定值（会被夹在 0 ~ 成长表里的最高等级之间）。
    ///
    /// 实现：撕掉这个属性上旧的成长便利贴 → 按 等级 × 每级加成 贴一张新的。
    /// 所以重复调用、降级、读档恢复都是安全的，永远不会叠错。
    /// </summary>
    /// <returns>实际生效的等级。成长表里没配这个属性时返回 0</returns>
    public int SetAttributeLevel(PlayerAttribute attribute, int level)
    {
        EnsureInitialized();

        ModifiableStat stat = stats.Get(attribute);
        if (stat == null) return 0;

        if (attributeConfig == null || !attributeConfig.TryGetGrowth(attribute, out AttributeGrowth growth))
        {
            Debug.LogWarning($"[成长] 成长表里没有配置「{attribute}」，等级设置被忽略。", this);
            return 0;
        }

        level = Mathf.Clamp(level, 0, growth.maxLevel);

        stat.RemoveAllFromSource(AttributeGrowthSource);
        if (level > 0)
        {
            stat.AddModifier(new StatModifier(growth.valuePerLevel * level, growth.modifierType, AttributeGrowthSource));
        }

        attributeLevels[attribute] = level;
        OnAttributeLevelChanged?.Invoke(attribute, level);
        return level;
    }

    /// <summary>升 / 降若干级（默认升 1 级）。技能树解锁技能时调用这个</summary>
    public int AddAttributeLevel(PlayerAttribute attribute, int delta = 1)
        => SetAttributeLevel(attribute, GetAttributeLevel(attribute) + delta);

    /// <summary>清空所有成长（重开局用）</summary>
    public void ResetAllAttributeLevels()
    {
        foreach (PlayerAttribute attr in System.Enum.GetValues(typeof(PlayerAttribute)))
        {
            stats.Get(attr)?.RemoveAllFromSource(AttributeGrowthSource);
        }
        attributeLevels.Clear();
    }

    // ==========================================
    // 穿透图层：引用计数 API
    // ==========================================

    /// <summary>请求切到穿透图层（可穿过敌人）。source 传申请方本身</summary>
    public void RequestPhaseThrough(object source)
    {
        if (source == null) return;
        if (playerLayer < 0 || phaseThroughLayer < 0) return;

        bool was = phaseThroughSources.Count > 0;
        phaseThroughSources.Add(source);

        if (!was && phaseThroughSources.Count > 0)
        {
            gameObject.layer = phaseThroughLayer;
        }
    }

    /// <summary>释放穿透请求。所有申请方都释放后才切回常态图层</summary>
    public void ReleasePhaseThrough(object source)
    {
        if (source == null) return;
        if (playerLayer < 0 || phaseThroughLayer < 0) return;

        bool was = phaseThroughSources.Count > 0;
        phaseThroughSources.Remove(source);

        if (was && phaseThroughSources.Count == 0)
        {
            gameObject.layer = playerLayer;
        }
    }

    /// <summary>强制清空。仅用于复活、重开局</summary>
    public void ForceClearPhaseThrough()
    {
        phaseThroughSources.Clear();
        if (playerLayer >= 0) gameObject.layer = playerLayer;
    }

    // ==========================================
    // 受伤
    // ==========================================

    /// <summary>
    /// 【统一受伤入口】与敌人侧的 EntityBase 使用同一份契约。
    ///
    /// 击退力度不由攻击方决定 —— 这里只把「来源在我左边还是右边」翻译成方向，
    /// 力度由 PlayerCombatConfig.hitKnockbackForce 配置、HitState 读取。
    /// </summary>
    public void TakeDamage(in DamageInfo info)
    {
        float dx = transform.position.x - info.sourcePosition.x;

        Vector2 hitDirection = Mathf.Abs(dx) > 0.01f
            ? new Vector2(Mathf.Sign(dx), 0f)
            : Vector2.zero;

        // 【招式自带击退】只在这一次受伤的同步调用期间有效：
        // health.TakeDamage → OnPlayerHit → 状态机切进 HitState，HitState.Enter 当场读取。
        // 调用结束立刻清空 —— 无敌中被打（伤害被忽略）时，这份覆盖也不会残留到下一次受击。
        PendingKnockbackOverride = info.overrideKnockback ? info.knockbackForce : (Vector2?)null;
        health.TakeDamage(info.amount, hitDirection, this);
        PendingKnockbackOverride = null;
    }

    /// <summary>
    /// 本次受击招式自带的击退力度（x = 远离来源方向的水平力度，y = 竖直）。
    /// 为空 = 用 PlayerCombatConfig 里的默认击飞力度。只在受伤的那一次同步调用期间有值。
    /// </summary>
    public Vector2? PendingKnockbackOverride { get; private set; }
}
