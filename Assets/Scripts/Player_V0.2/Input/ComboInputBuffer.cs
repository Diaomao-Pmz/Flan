using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

/// <summary>
/// 【连招匹配引擎】
///
/// ==========================================================
/// 【批次G 改动】招式来源从「Inspector 固定列表」变成「由装备的武器现场提供」
///
/// 改造前：rootNodes 手动拖在预制体上 → 连招树和角色焊死，换武器 = 改预制体。
///
/// 改造后每次匹配时现场去问武器中枢：
///   currentNode == null  → 问「这个键对应的武器」要起手招 (openers)
///   currentNode != null  → 候选 = 上一招的 childNodes（同武器固定衔接）
///                                 ∪ 这个键对应武器的 followUps（跨武器接续）
///
/// 关键点：武器之间【互不认识】。
/// 主武器不需要知道副武器是什么，「主A → 副B → 主C」是引擎运行时拼出来的。
/// 加第 5 把武器时，其余武器一个都不用改。
///
/// 【向后兼容】没挂 WeaponLoadout、或主副武器都为空时，
/// 自动回退到旧的 rootNodes 配置 —— 你现有的 4 个根节点照常工作，
/// 可以慢慢往武器资产里搬，不必一次迁完。
/// ==========================================================
///
/// 【批次F 的修复保持不变】
/// 连招生命周期由闲置计时器管理，不依赖动画事件，
/// 因此「打一半被冲刺取消 → 指针永久残留 → 白嫖二段」的漏洞不会复发。
/// </summary>
public class ComboInputBuffer : MonoBehaviour
{
    [Header("旧版连招树 (未装备武器时的回退配置)")]
    [Tooltip("挂了 WeaponLoadout 且装备了武器时，本列表会被忽略")]
    public List<ComboNode> rootNodes = new List<ComboNode>();

    public ComboNode currentNode { get; private set; }

    /// <summary>当前连招进行到第几段。起手为 1，未进入连招为 0</summary>
    public int comboDepth { get; private set; } = 0;

    [Header("工业级 ACT 手感配置")]
    [Tooltip("攻击预输入缓存时间。位移指令的缓存在 PlayerCommandRouter 上单独配置")]
    public float bufferLifespan = 0.2f;

    [Tooltip(
        "开启：被冲刺/跳跃打断后，在窗口期内回来仍能接下一段（冲刺取消接招）\n" +
        "关闭：任何非攻击动作都立刻断档，回到起手")]
    public bool allowCancelWindowRecovery = true;

    [Header("Debug")]
    public bool verboseLog = false;

    // ---- 预输入缓存 ----
    private bool hasBufferedInput = false;
    private InputCmd bufferedCmd;
    private float bufferedHoldTime = 0f;
    private float bufferTimer = 0f;

    // ---- 连招闲置计时器 ----
    private float comboIdleTimer = 0f;

    private PlayerStateMachine sm;
    private PlayerController player;
    private PlayerState state;
    private WeaponLoadout weapons;

    // 复用列表，避免每帧 new 产生 GC
    private readonly List<ComboNode> candidateBuffer = new List<ComboNode>(16);

    void Awake()
    {
        sm = GetComponent<PlayerStateMachine>();
        player = GetComponent<PlayerController>();
        state = GetComponent<PlayerState>();
        weapons = GetComponent<WeaponLoadout>();
    }

    private float ComboWindowTolerance
        => state != null ? state.stats.comboWindowTolerance.Value : 0f;

    /// <summary>玩家当前是否仍处于战斗姿态（连招中或蓄力架势中）</summary>
    private bool IsInCombatStance
        => sm.currentState == sm.comboState || sm.currentState == sm.chargeState;

    /// <summary>当前正在使用的武器（用于远程发射器取子弹）</summary>
    public WeaponMoveSet ActiveWeapon { get; private set; }

    void Update()
    {
        if (hasBufferedInput)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0) hasBufferedInput = false;
        }

        TickComboIdle();

        if (player != null)
        {
            if (player.isMainAttackHeld && !player.isMainChargeConsumed)
                TryAutoExecuteCharge(InputCmd.MainAttack, player.mainAttackHoldTime);

            if (player.isSubAttackHeld && !player.isSubChargeConsumed)
                TryAutoExecuteCharge(InputCmd.SubAttack, player.subAttackHoldTime);
        }
    }

    /// <summary>
    /// 不依赖任何动画事件的连招生命周期管理。
    /// 只要玩家离开战斗姿态，计时器就开始走字；超过派生窗口期就断档。
    /// 被冲刺/跳跃/滑铲取消，或动画正常播完，全都走这同一条路径。
    /// </summary>
    private void TickComboIdle()
    {
        if (currentNode == null) return;

        if (IsInCombatStance)
        {
            comboIdleTimer = 0f;
            return;
        }

        if (!allowCancelWindowRecovery)
        {
            if (verboseLog) Debug.Log("[连招] 离开战斗姿态，立刻断档");
            ResetCombo();
            return;
        }

        comboIdleTimer += Time.deltaTime;

        float limit = currentNode.comboWindow + ComboWindowTolerance;
        if (comboIdleTimer > limit)
        {
            if (verboseLog) Debug.Log($"[连招] 闲置 {comboIdleTimer:F2}s 超过 {limit:F2}s，断档");
            ResetCombo();
        }
    }

    // ==================================================
    // 输入接口
    // ==================================================

    public void OnReceiveInput(InputCmd cmd)
    {
        if (cmd != InputCmd.MainAttack && cmd != InputCmd.SubAttack) return;
        if (sm.currentState == sm.flyState) return;
        if (sm.currentState == sm.hitState) return;

        hasBufferedInput = true;
        bufferedCmd = cmd;
        bufferedHoldTime = 0f;
        bufferTimer = bufferLifespan;

        if (sm.currentState != sm.comboState) TryAdvanceCombo();
    }

    public void OnReceiveChargeRelease(InputCmd cmd, float holdTime)
    {
        if (sm.currentState == sm.hitState || sm.currentState == sm.flyState) return;

        hasBufferedInput = true;
        bufferedCmd = cmd;
        bufferedHoldTime = holdTime;
        bufferTimer = bufferLifespan;

        if (sm.currentState != sm.comboState) TryAdvanceCombo();
    }

    private void TryAutoExecuteCharge(InputCmd cmd, float currentHoldTime)
    {
        CollectCandidates(cmd);
        ComboNode match = FindBestMatch(candidateBuffer, cmd, currentHoldTime);

        if (match != null && match.isChargeSkill)
        {
            if (cmd == InputCmd.MainAttack) player.ConsumeMainCharge();
            if (cmd == InputCmd.SubAttack) player.ConsumeSubCharge();

            hasBufferedInput = false;
            SetCurrentNode(match, cmd);
            sm.ChangeState(sm.comboState);
        }
    }

    // ==================================================
    // 核心匹配
    // ==================================================

    public bool TryAdvanceCombo()
    {
        if (!hasBufferedInput) return false;

        // 取消权限：当前招式允许被这个键派生吗？
        // 把某一段的「主武器」取消勾选，玩家就必须换手才能续上 ——
        // 这正是引导主副配合连招的手段。
        if (currentNode != null && !currentNode.CanBeCanceledBy(bufferedCmd))
        {
            if (verboseLog)
                Debug.Log($"[连招] {currentNode.nodeName} 不允许被 {bufferedCmd} 派生");
            return false;
        }

        CollectCandidates(bufferedCmd);
        ComboNode match = FindBestMatch(candidateBuffer, bufferedCmd, bufferedHoldTime);

        if (match != null)
        {
            hasBufferedInput = false;
            SetCurrentNode(match, bufferedCmd);
            sm.ChangeState(sm.comboState);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 【批次G 核心】现场解析候选招式。
    ///
    /// 起手时：问「这个键对应的武器」要 openers
    /// 接续时：上一招的 childNodes（同武器固定衔接）
    ///         ∪ 这个键对应武器的 followUps（跨武器接续）
    ///
    /// 注意 followUps 不关心上一段是谁打的 —— 这就是为什么
    /// 主武器不需要认识副武器，也能拼出「主A → 副B → 主C」。
    /// </summary>
    private void CollectCandidates(InputCmd cmd)
    {
        candidateBuffer.Clear();

        WeaponMoveSet weapon = weapons != null ? weapons.GetWeaponForCommand(cmd) : null;

        // ---- 起手 ----
        if (currentNode == null)
        {
            if (weapon != null && weapon.openers != null && weapon.openers.Count > 0)
            {
                AddCandidates(weapon.openers, 1);
            }
            else
            {
                // 回退：没装武器就用旧的 rootNodes
                AddCandidates(rootNodes, 1);
            }
            return;
        }

        // ---- 接续 ----
        int nextDepth = comboDepth + 1;

        // 同武器内部的固定衔接（三连突刺这类）
        AddCandidates(currentNode.childNodes, nextDepth);

        // 跨武器接续
        if (weapon != null) AddCandidates(weapon.followUps, nextDepth);

        // 两边都没有 → 回退到旧配置，保证迁移期不断链
        if (candidateBuffer.Count == 0 && weapon == null)
        {
            AddCandidates(rootNodes, nextDepth);
        }
    }

    private void AddCandidates(List<ComboNode> source, int depth)
    {
        if (source == null) return;

        for (int i = 0; i < source.Count; i++)
        {
            ComboNode node = source[i];
            if (node == null) continue;
            if (!node.IsDepthAllowed(depth)) continue;
            if (candidateBuffer.Contains(node)) continue;   // 同一招可能同时在两个来源里

            candidateBuffer.Add(node);
        }
    }

    private ComboNode FindBestMatch(List<ComboNode> nodes, InputCmd triggerCmd, float holdTime)
    {
        if (nodes == null) return null;

        ComboNode bestMatch = null;
        int maxSequenceLength = -1;   // 优先级：要求越多的连招优先级越高

        foreach (var node in nodes)
        {
            if (node == null) continue;

            // 1. 环境限制
            if (node.castCondition == CastCondition.GroundOnly && !sm.IsGrounded()) continue;
            if (node.castCondition == CastCondition.AirOnly && sm.IsGrounded()) continue;

            // 2. 前置状态限制
            if (!IsRequiredStateMet(node.requiredState)) continue;

            // 3. 蓄力条件
            if (node.isChargeSkill)
            {
                if (holdTime < node.requiredChargeTime) continue;
            }
            else
            {
                // 普通技能只能由瞬间按下触发，防止松开蓄力时误打出普攻
                if (holdTime > 0f) continue;
            }

            // 4. 组合键序列
            if (node.inputSequence.Count == 0) continue;
            if (node.inputSequence[node.inputSequence.Count - 1] != triggerCmd) continue;

            bool isSequenceMatched = true;
            for (int i = 0; i < node.inputSequence.Count - 1; i++)
            {
                if (!IsDirectionalCommandHeld(node.inputSequence[i]))
                {
                    isSequenceMatched = false;
                    break;
                }
            }
            if (!isSequenceMatched) continue;

            // 5. 优先级对决
            if (node.inputSequence.Count > maxSequenceLength)
            {
                maxSequenceLength = node.inputSequence.Count;
                bestMatch = node;
            }
        }

        return bestMatch;
    }

    private bool IsRequiredStateMet(RequiredState req)
    {
        if (req == RequiredState.Any) return true;
        if (req == RequiredState.IdleOrRun) return (sm.currentState == sm.idleState || sm.currentState == sm.runState);
        if (req == RequiredState.Dash) return sm.currentState == sm.dashState;
        if (req == RequiredState.Slide) return sm.currentState == sm.slideState;
        if (req == RequiredState.Crouch) return sm.currentState == sm.crouchState;
        return false;
    }

    private bool IsDirectionalCommandHeld(InputCmd cmd)
    {
        if (cmd == InputCmd.Up) return sm.playerController.moveInput.y > 0.1f;
        if (cmd == InputCmd.Down) return sm.playerController.moveInput.y < -0.1f;
        if (cmd == InputCmd.Left) return sm.playerController.moveInput.x < -0.1f;
        if (cmd == InputCmd.Right) return sm.playerController.moveInput.x > 0.1f;
        return false;
    }

    private void SetCurrentNode(ComboNode node, InputCmd triggerCmd)
    {
        currentNode = node;
        comboDepth++;
        comboIdleTimer = 0f;

        // 记下这一段是哪把武器打的，供远程发射器取正确的子弹
        ActiveWeapon = weapons != null ? weapons.GetWeaponForCommand(triggerCmd) : null;

        if (verboseLog)
        {
            string w = ActiveWeapon != null ? ActiveWeapon.displayName : "无武器";
            Debug.Log($"[连招] 第{comboDepth}段：{node.nodeName}（{w}）");
        }
    }

    /// <summary>
    /// 【兼容层】原本由动画事件 OnAttackAnimationEnd 调用。
    /// 现在生命周期由 TickComboIdle 全权管理，本方法只是把计时器归零。
    /// 保留它是为了不破坏已有的动画事件绑定。
    /// </summary>
    public void StartGracePeriod()
    {
        comboIdleTimer = 0f;
    }

    public void ResetCombo()
    {
        currentNode = null;
        comboDepth = 0;
        comboIdleTimer = 0f;
        hasBufferedInput = false;
        ActiveWeapon = null;
    }

    /// <summary>当前招式能否被位移动作打断。供 PlayerCommandRouter 查询</summary>
    public bool CanCurrentNodeBeCanceledByMovement()
        => currentNode == null || currentNode.CanBeCanceledByMovement;
}
