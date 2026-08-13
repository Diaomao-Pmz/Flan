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
    public PlayerMovementConfig movementConfig;
    public PlayerCombatConfig combatConfig;

    [Header("核心数据总线")]
    public PlayerStats stats = new PlayerStats();
    public PlayerHealth health = new PlayerHealth();

    [Header("图层设置")]
    [Tooltip("玩家常态所在图层名")]
    public string playerLayerName = "Player";
    [Tooltip("可穿透敌人时切换到的图层名")]
    public string phaseThroughLayerName = "Invincible";

    private int playerLayer = -1;
    private int phaseThroughLayer = -1;

    // 穿透图层的引用计数
    private readonly HashSet<object> phaseThroughSources = new HashSet<object>();

    private bool isInitialized = false;

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

        if (combatConfig == null)
        {
            combatConfig = ScriptableObject.CreateInstance<PlayerCombatConfig>();
            Debug.LogWarning(
                "[PlayerState] 未指定 PlayerCombatConfig，已临时创建默认配置。\n" +
                "请右键 Create > Flandre > Player > Combat Config 生成资产并拖上来。" +
                "（临时配置不会保存）", this);
        }

        stats.Init(movementConfig);
        health.Init(combatConfig);

        // 图层解析。找不到就退化为「不切图层」，而不是崩溃或切到 layer 0
        playerLayer = LayerMask.NameToLayer(playerLayerName);
        phaseThroughLayer = LayerMask.NameToLayer(phaseThroughLayerName);

        if (playerLayer < 0)
            Debug.LogWarning($"[PlayerState] 找不到图层「{playerLayerName}」，穿透功能已禁用。", this);
        if (phaseThroughLayer < 0)
            Debug.LogWarning($"[PlayerState] 找不到图层「{phaseThroughLayerName}」，穿透功能已禁用。", this);
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

        health.TakeDamage(info.amount, hitDirection, this);
    }
}
