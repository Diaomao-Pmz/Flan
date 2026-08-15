using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

/// <summary>
/// 【连招匹配引擎】
///
/// ==========================================================
/// 【批次J 改动】蓄力从「蓄够自动放」改为「松手才放，按等级选招」
///
/// 删掉的：TryAutoExecuteCharge —— 每帧遍历子树看蓄力够没够，够了就自动打出去。
///         这条路径整个消失，蓄力的主导权交给 ChargeState。
///
/// 新增的：TryReleaseCharge(cmd, level)
///         在【等级 <= 当前等级】的候选里挑最高的那一个。
///         所以蓄到 2 级松手出 AA2，蓄到 3 级松手出 AA3。
///
/// 匹配优先级也随之调整为两级排序：
///   1. 按键序列越长越优先  → 保证「s+d+AA」这类方向变招压过裸 AA
///   2. 序列一样长时，蓄力等级越高越优先 → 保证蓄满时出的是 AA3 而不是 AA1
/// ==========================================================
/// </summary>
public class ComboInputBuffer : MonoBehaviour
{
    [Header("旧版连招树 (未装备武器时的回退配置)")]
    [Tooltip("挂了 WeaponLoadout 且装备了武器时，本列表会被忽略")]
    public List<ComboNode> rootNodes = new List<ComboNode>();

    public ComboNode currentNode { get; private set; }

    /// <summary>当前连招进行到第几段。起手为 1，未进入连招为 0</summary>
    public int comboDepth { get; private set; } = 0;

    /// <summary>最近一次出招是由哪个键触发的。ChargeState 用它判断在蓄哪把武器</summary>
    public InputCmd LastTriggerCmd { get; private set; } = InputCmd.MainAttack;

    /// <summary>
    /// 打完当前这一招之后，蓄力应该从第几级起步。
    /// 这是「连段是蓄力的助跑」这一设计的传递通道：
    ///   A2 的 chargeStartLevelAfter = 1 → 打完 A2 按住，直接从 AA1 起步
    /// </summary>
    public int PendingChargeStartLevel
        => currentNode != null ? currentNode.chargeStartLevelAfter : 0;

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

    /// <summary>玩家当前是否仍处于战斗姿态（连招中或蓄力中）</summary>
    private bool IsInCombatStance
        => sm.currentState == sm.comboState || sm.currentState == sm.chargeState;

    /// <summary>当前正在使用的武器（供远程发射器取正确的子弹）</summary>
    public WeaponMoveSet ActiveWeapon { get; private set; }

    void Update()
    {
        if (hasBufferedInput)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0) hasBufferedInput = false;
        }

        TickComboIdle();

        // 【批次J 删除】TryAutoExecuteCharge
        // 原先每帧在这里检查「蓄力够没够，够了自动打出去」。
        // 现在蓄力的主导权归 ChargeState —— 松手才放。
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
        bufferTimer = bufferLifespan;

        if (sm.currentState != sm.comboState) TryAdvanceCombo();
    }

    /// <summary>
    /// 【批次J 改动】松开攻击键。
    ///
    /// 蓄力的释放由 ChargeState 主导（它才知道当前蓄到几级），
    /// 所以本方法不再负责出招，只是把预输入缓存清掉 ——
    /// 免得松手后缓存里那条按下指令又跑出来打一发普攻。
    /// </summary>
    public void OnAttackReleased(InputCmd cmd)
    {
        if (hasBufferedInput && bufferedCmd == cmd) hasBufferedInput = false;
    }

    // ==================================================
    // 普通连招推进
    // ==================================================

    public bool TryAdvanceCombo()
    {
        if (!hasBufferedInput) return false;

        // 取消权限：当前招式允许被这个键派生吗？
        if (currentNode != null && !currentNode.CanBeCanceledBy(bufferedCmd))
        {
            if (verboseLog)
                Debug.Log($"[连招] {currentNode.nodeName} 不允许被 {bufferedCmd} 派生");
            return false;
        }

        CollectCandidates(bufferedCmd);

        // 普通推进只找非蓄力招 —— 蓄力招走 TryReleaseCharge
        ComboNode match = FindBestMatch(candidateBuffer, bufferedCmd, requiredChargeLevel: 0);

        if (match != null)
        {
            hasBufferedInput = false;
            SetCurrentNode(match, bufferedCmd);
            sm.ChangeState(sm.comboState);
            return true;
        }

        return false;
    }

    // ==================================================
    // 蓄力释放
    // ==================================================

    /// <summary>
    /// 由 ChargeState 在松手时调用。
    /// 在【等级 <= level】的蓄力招里挑最高的那一个打出去。
    /// </summary>
    /// <returns>是否成功打出了蓄力招</returns>
    public bool TryReleaseCharge(InputCmd cmd, int level)
    {
        if (level <= 0) return false;

        CollectCandidates(cmd);

        ComboNode match = FindBestMatch(candidateBuffer, cmd, requiredChargeLevel: level);

        if (match == null)
        {
            if (verboseLog)
                Debug.Log($"[连招] 蓄力 {level} 级松手，但没有匹配的蓄力招（招式表可能没配全）");
            return false;
        }

        hasBufferedInput = false;
        SetCurrentNode(match, cmd);
        sm.ChangeState(sm.comboState);

        if (verboseLog) Debug.Log($"[连招] 蓄力释放：{match.nodeName} (等级 {match.chargeLevel}/{level})");
        return true;
    }

    // ==================================================
    // 突刺（单按 shift）
    // ==================================================

    /// <summary>
    /// 【批次L 新增】突刺 —— 攻击中【单按】冲刺键（不带方向）。
    ///
    /// 这是连招的一部分，不是位移打断：
    ///   方向 + shift → 打断攻击，普通冲刺（走 PlayerCommandRouter 的常规路径）
    ///   单按   shift → 突刺，接在当前招式之后
    ///
    /// 有无方向键是唯一的分流依据，判断在路由器里做，本方法只管匹配。
    ///
    /// 【候选从哪来】
    /// 突刺属于"你正在用的那把武器"，但触发键是 Dash 而不是攻击键，
    /// 所以要用【上一段的武器】去取候选，用【Dash】去匹配 inputSequence。
    /// 这就是下面 CollectCandidates 要区分 triggerCmd 与 weaponCmd 的原因。
    /// </summary>
    /// <returns>是否成功打出了突刺</returns>
    public bool TryThrust()
    {
        // 突刺必须接在某一招之后 —— 平地单按 shift 就是普通冲刺
        if (currentNode == null) return false;

        CollectCandidates(InputCmd.Dash, weaponCmd: LastTriggerCmd);

        ComboNode match = FindBestMatch(candidateBuffer, InputCmd.Dash, requiredChargeLevel: 0);

        if (match == null)
        {
            if (verboseLog) Debug.Log("[连招] 单按 shift 但没有匹配的突刺招式");
            return false;
        }

        hasBufferedInput = false;
        SetCurrentNode(match, LastTriggerCmd);   // 武器归属仍算在原武器头上
        sm.ChangeState(sm.comboState);

        if (verboseLog) Debug.Log($"[连招] 突刺：{match.nodeName}");
        return true;
    }

    // ==================================================
    // 候选解析
    // ==================================================

    /// <summary>
    /// 现场解析候选招式。
    ///
    /// 起手时：问「这个键对应的武器」要 openers
    /// 接续时：上一招的 childNodes（同武器固定衔接）
    ///         ∪ 这个键对应武器的 followUps（跨武器接续）
    ///
    /// followUps 不关心上一段是谁打的 —— 这就是为什么主武器不需要认识副武器，
    /// 也能拼出「主A → 副B → 主C」。
    /// </summary>
    /// <param name="cmd">用来匹配 inputSequence 末位的触发键</param>
    /// <param name="weaponCmd">
    /// 用来决定"从哪把武器取候选"的键。
    /// 默认与 cmd 相同；突刺时不同 —— 触发键是 Dash，但候选来自上一段的武器。
    /// </param>
    private void CollectCandidates(InputCmd cmd, InputCmd? weaponCmd = null)
    {
        candidateBuffer.Clear();

        InputCmd lookupCmd = weaponCmd ?? cmd;
        WeaponMoveSet weapon = weapons != null ? weapons.GetWeaponForCommand(lookupCmd) : null;

        if (currentNode == null)
        {
            if (weapon != null && weapon.openers != null && weapon.openers.Count > 0)
                AddCandidates(weapon.openers, 1);
            else
                AddCandidates(rootNodes, 1);   // 回退：没装武器就用旧配置
            return;
        }

        int nextDepth = comboDepth + 1;

        AddCandidates(currentNode.childNodes, nextDepth);          // 同武器固定衔接
        if (weapon != null) AddCandidates(weapon.followUps, nextDepth);  // 跨武器接续

        if (candidateBuffer.Count == 0 && weapon == null)
            AddCandidates(rootNodes, nextDepth);
    }

    private void AddCandidates(List<ComboNode> source, int depth)
    {
        if (source == null) return;

        for (int i = 0; i < source.Count; i++)
        {
            ComboNode node = source[i];
            if (node == null) continue;
            if (!node.IsDepthAllowed(depth)) continue;
            if (candidateBuffer.Contains(node)) continue;

            candidateBuffer.Add(node);
        }
    }

    /// <summary>
    /// 匹配引擎。
    /// </summary>
    /// <param name="requiredChargeLevel">
    /// 0 = 只找非蓄力招；
    /// &gt;0 = 只找蓄力招，且只接受 chargeLevel &lt;= 本值的，取其中最高的一个
    /// </param>
    private ComboNode FindBestMatch(List<ComboNode> nodes, InputCmd triggerCmd, int requiredChargeLevel)
    {
        if (nodes == null) return null;

        ComboNode bestMatch = null;
        int bestSequenceLength = -1;
        int bestChargeLevel = -1;

        foreach (var node in nodes)
        {
            if (node == null) continue;

            // ---- 蓄力/非蓄力分流 ----
            if (requiredChargeLevel > 0)
            {
                if (!node.IsValidChargeNode) continue;
                if (node.chargeLevel > requiredChargeLevel) continue;   // 还没蓄到这一级
            }
            else
            {
                if (node.isChargeSkill) continue;   // 普通推进不碰蓄力招
            }

            // ---- 环境限制 ----
            if (node.castCondition == CastCondition.GroundOnly && !sm.IsGrounded()) continue;
            if (node.castCondition == CastCondition.AirOnly && sm.IsGrounded()) continue;

            // ---- 前置状态限制 ----
            if (!IsRequiredStateMet(node.requiredState)) continue;

            // ---- 组合键序列 ----
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

            // ---- 优先级对决（两级排序）----
            // 1. 按键序列越长越优先 —— 让「s+d+AA」这类方向变招压过裸 AA
            // 2. 序列一样长时蓄力等级越高越优先 —— 蓄满时出 AA3 而不是 AA1
            int seqLen = node.inputSequence.Count;

            if (seqLen > bestSequenceLength
                || (seqLen == bestSequenceLength && node.chargeLevel > bestChargeLevel))
            {
                bestSequenceLength = seqLen;
                bestChargeLevel = node.chargeLevel;
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
        LastTriggerCmd = triggerCmd;

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
