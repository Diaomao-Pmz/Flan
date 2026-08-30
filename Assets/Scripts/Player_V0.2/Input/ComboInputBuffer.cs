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
///         这条路径整个消失，蓄力的主导权交给「松手」这一刻。
///         （P3 之后蓄力的进度由 PlayerChargeSystem 记，结算在本文件的 OnAttackReleased）
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

    /// <summary>最近一次出招是由哪个键触发的。ComboState 用它认领后摇该记在哪只手上</summary>
    public InputCmd LastTriggerCmd { get; private set; } = InputCmd.MainAttack;

    /// <summary>
    /// 本次出招是不是「强行打出」的缩水版蓄力（CD 没走完就长按放出来的）。
    ///
    /// 和 LastTriggerCmd 一样，它是【本次出招的属性】而不是持久状态：
    /// 蓄力释放时置位，任何普通招式打出时清零。
    ///
    /// 消费方要在 Enter 时【拍快照】而不是全程去问 —— 因为招式演到一半时
    /// 连段可能被位移打断而 ResetCombo，那时再问就问不到了。
    /// ComboState 对 activeNode / ownerCmd 用的是同一个套路。
    /// </summary>
    public bool IsCurrentAttackWeakened => isCurrentAttackWeakened;

    private bool isCurrentAttackWeakened;

    /// <summary>
    /// 【P2 重写】蓄力的目标等级 —— 由【连段深度】决定，不再看上一招的配置字段。
    ///
    /// 规则：
    ///     蓄力目标等级 = 上次普攻的段数（封顶 3）
    ///     蓄力起始等级 = 目标等级 - 1
    /// 一次蓄力只升一级，蓄满即封顶。
    ///
    /// | 上次普攻打到 | 起始 | 目标 |
    /// |---|---|---|
    /// | 没打过（原地起手） | 0 | AA1 |
    /// | 第 1 段 | 0 | AA1 |
    /// | 第 2 段 | 1 | AA2 |
    /// | 第 3 段 | 2 | AA3 |
    ///
    /// 【为什么用连段深度而不是节点上的字段】
    /// 因为「换手蓄力不加段数」这条规则要求等级与用哪只手无关 ——
    /// 只跟"打到第几段"有关。连段深度本来就是全局的，
    /// 而节点字段是每招式各配一份，换手时会取到另一把武器的配置，对不上。
    /// </summary>
    public int ChargeTargetLevel
        => Mathf.Clamp(Mathf.Max(1, comboDepth), 1, WeaponMoveSet.MaxChargeLevel);

    /// <summary>蓄力起始等级 = 目标 - 1</summary>
    public int ChargeStartLevel => ChargeTargetLevel - 1;

    /// <summary>本次蓄力是否接在普攻后面（用于判断要不要吃蓄力加速）</summary>
    public bool IsChargeAfterCombo => comboDepth > 0;

    [Header("工业级 ACT 手感配置")]
    [Tooltip(
        "攻击预输入的宽恕期（秒）。\n\n" +
        "⚠️ 这段时间【只在闸门开着时消耗】——\n" +
        "该手还在出招或后摇中时，缓存不计时，闸门一开立刻兑现。\n" +
        "所以它衡量的是「闸门开了之后还愿意等你多久」，\n" +
        "而不是「按下之后多久作废」。位移指令的缓存在 PlayerCommandRouter 上单独配置。")]
    public float bufferLifespan = 0.2f;

    [Tooltip(
        "手还忙着时，预输入最多滞留多久（秒）。超过就丢弃。\n\n" +
        "防止超长动画里很早按下的一次点按在一秒多以后突然打出来 ——\n" +
        "那会让玩家觉得角色在自己动。填得比最长的招式动画略长即可。")]
    public float maxBufferedHoldTime = 1.5f;

    [Tooltip(
        "开启：被冲刺/跳跃打断后，在窗口期内回来仍能接下一段（冲刺取消接招）\n" +
        "关闭：任何非攻击动作都立刻断档，回到起手")]
    public bool allowCancelWindowRecovery = true;

    [Header("后摇")]
    [Tooltip(
        "后摇期间锁住移动（默认开启）。\n\n" +
        "按「攻击时不能走动」的基础规则，后摇属于攻击的一部分，\n" +
        "所以攻击动画结束后的这段时间也不该能走。\n" +
        "关掉则退回旧行为：动画一结束就能自由走动。")]
    public bool lockMovementDuringRecovery = true;

    [Header("Debug")]
    public bool verboseLog = false;

    // ---- 预输入缓存 ----
    private bool hasBufferedInput = false;
    private InputCmd bufferedCmd;
    private float bufferTimer = 0f;

    /// <summary>缓存在「闸门没开」状态下已经滞留了多久。用于兜底丢弃，见 maxBufferedHoldTime</summary>
    private float bufferHeldWhileBusy = 0f;

    // ---- 连招闲置计时器 ----
    private float comboIdleTimer = 0f;

    private PlayerStateMachine sm;
    private PlayerController player;
    private PlayerState state;
    private WeaponLoadout weapons;
    private PlayerChargeSystem chargeSystem;

    // 复用列表，避免每帧 new 产生 GC
    private readonly List<ComboNode> candidateBuffer = new List<ComboNode>(16);

    // ==================================================
    // 【P1a 新增】每只手一个独立的后摇计时器
    //
    // 规则：同手出招必须等后摇结束；换手完全不查，可以抢拍。
    //
    // 比喻：不再是"整个人在出招所以不能动"，
    //       而是"左手正忙，右手空着" —— 两只手各算各的账。
    //
    // 时长读节点上的 recoveryTime，不读动画长度 ——
    // 因为动画后期会替换，参数化才不会每换一次美术就要重调手感。
    // ==================================================
    private readonly float[] handBusyUntil = new float[2];

    /// <summary>
    /// 每只手最近一次出招的编号。
    ///
    /// 【为什么需要它】同手接招时的执行顺序是：
    ///     新招 SetCurrentNode（标记本手"出招中"）
    ///       → ChangeState → 旧招 Exit（写后摇）
    /// 旧招的 Exit 会把新招刚打上的"出招中"标记覆盖掉，
    /// 导致新招的动画期间手是空闲的，后摇从新招【开始】那一刻算起而不是结束。
    ///
    /// 有了编号，Exit 只在"我还是最新那一次"时才写后摇。
    /// </summary>
    private readonly int[] handAttackId = new int[2];
    private int attackIdCounter;

    /// <summary>
    /// 后摇期间按下的长按意图，存着等后摇结束再兑现。
    ///
    /// 【为什么需要它】0.15 秒的点按/长按判定发生在按键松开或超时的那一刻。
    /// 如果那时手还在后摇里，长按分支会直接 return，
    /// 而松手时又因为"已经判定过"不会补发点按 ——
    /// 这一次按键就彻底消失了。
    ///
    /// 表现出来就是：后摇越长被吞得越多，感知延迟远大于参数，且复现不稳定
    /// （取决于手指按了多久、跨没跨过 0.15 秒）。
    /// </summary>
    private readonly bool[] pendingHold = new bool[2];

    private static int HandIndex(InputCmd cmd)
        => cmd == InputCmd.SubAttack ? 1 : 0;

    /// <summary>这只手现在能不能出招（招式演完且后摇结束了吗）</summary>
    public bool IsHandReady(InputCmd cmd)
        => Time.time >= handBusyUntil[HandIndex(cmd)];

    /// <summary>
    /// 这只手正在出招中（招式还没演完）。
    ///
    /// 与「后摇中」是两种不同的忙碌状态：
    ///   出招中 —— 招式还在演，用 float.MaxValue 表示无限期锁死，没有倒计时
    ///   后摇中 —— 招式演完了，正在走 recoveryTime 的倒计时
    /// 对玩家来说都是"这只手不能用"，但排查问题时必须分清。
    /// </summary>
    public bool IsHandAttacking(InputCmd cmd)
        => handBusyUntil[HandIndex(cmd)] >= float.MaxValue * 0.5f;

    /// <summary>
    /// 该手剩余后摇秒数。供 UI 显示。
    /// 出招中（无倒计时）时返回 -1，调用方据此区分两种忙碌。
    /// </summary>
    public float GetHandRecoveryRemaining(InputCmd cmd)
    {
        if (IsHandAttacking(cmd)) return -1f;
        return Mathf.Max(0f, handBusyUntil[HandIndex(cmd)] - Time.time);
    }

    /// <summary>
    /// 把某只手标记为「正在出招」—— 在招式结束之前一直锁死。
    ///
    /// 用一个很大的时间戳表示"暂时无限期"，等 BeginHandRecovery 来解锁。
    /// </summary>
    /// <returns>本次出招的编号，供 Exit 时核对</returns>
    private int MarkHandBusy(InputCmd cmd)
    {
        int i = HandIndex(cmd);
        handBusyUntil[i] = float.MaxValue;
        handAttackId[i] = ++attackIdCounter;
        return handAttackId[i];
    }

    /// <summary>取某只手当前的出招编号。ComboState 在 Enter 时记下它</summary>
    public int GetHandAttackId(InputCmd cmd) => handAttackId[HandIndex(cmd)];

    /// <summary>
    /// 【修正】招式结束时才开始算后摇。
    ///
    /// 原先是从【出招那一刻】起算，于是：
    /// 动画 0.5 秒、后摇填 0.35 秒的话，后摇在动画演完之前就走完了 ——
    ///   ① 同手能打断自己的动画，违反「同手必须等后摇结束」
    ///   ② 调这个参数完全感觉不到变化，因为总被动画长度盖过
    ///
    /// 比喻：原先是"下单后 30 分钟内不接新单"，但做菜就花了 40 分钟，
    ///       那 30 分钟形同虚设。改成"上菜后再歇 30 分钟"。
    ///
    /// 现在 同手封锁 = 动画演完 + recoveryTime，
    /// 这个参数才真正对应「后摇」，而且动画换了也不用重调。
    /// </summary>
    public void BeginHandRecovery(InputCmd cmd, float seconds, int attackId = -1)
    {
        int i = HandIndex(cmd);

        // 核对编号：这只手已经开始了更新的一次出招，就别拿旧招的后摇去覆盖它
        if (attackId >= 0 && attackId != handAttackId[i])
        {
            if (verboseLog)
                Debug.Log($"[连招] {cmd} 的旧招后摇被忽略（已有更新的出招）");
            return;
        }

        handBusyUntil[i] = Time.time + Mathf.Max(0f, seconds);

        if (verboseLog)
            Debug.Log($"[连招] {cmd} 进入后摇 {seconds:F2}s");
    }

    /// <summary>
    /// 任意一只手正忙（出招中或后摇中）。
    ///
    /// 供 Idle/Run 查询：后摇属于攻击的一部分，按「攻击时不能走动」的基础规则，
    /// 这段时间也该锁住移动。
    ///
    /// 注意是【任意一只】而不是【两只都】—— 换手连招时另一只手往往是空闲的，
    /// 若要求两只手都忙才锁，等于整套连招都能自由走位，规则形同虚设。
    /// </summary>
    public bool IsAnyHandBusy()
        => !IsHandReady(InputCmd.MainAttack) || !IsHandReady(InputCmd.SubAttack);

    /// <summary>
    /// 后摇是否应当锁住移动。Idle/Run 查这个。
    /// 关掉开关就退回旧行为（后摇期间可自由走动）。
    /// </summary>
    public bool IsMovementLockedByRecovery()
        => lockMovementDuringRecovery && IsAnyHandBusy();

    /// <summary>清空两只手的后摇。受击、死亡、切场景时用</summary>
    public void ClearHandRecovery()
    {
        handBusyUntil[0] = 0f;
        handBusyUntil[1] = 0f;
        pendingHold[0] = false;
        pendingHold[1] = false;
    }

    void Awake()
    {
        sm = GetComponent<PlayerStateMachine>();
        player = GetComponent<PlayerController>();
        state = GetComponent<PlayerState>();
        weapons = GetComponent<WeaponLoadout>();
        chargeSystem = GetComponent<PlayerChargeSystem>();
    }

    private float ComboWindowTolerance
        => state != null ? state.stats.comboWindowTolerance.Value : 0f;

    /// <summary>玩家当前是否仍处于战斗姿态（连招中或蓄力中）</summary>
    /// <summary>
    /// 玩家是否仍处于战斗姿态（连招中或任一只手在蓄力）。
    ///
    /// 【P3 改动】蓄力不再是一个 State，所以不能再用 currentState 判断，
    /// 改问蓄力系统"有没有哪只手在蓄"。
    /// </summary>
    private bool IsInCombatStance
        => sm.currentState == sm.comboState
           || (chargeSystem != null && chargeSystem.IsAnyCharging);

    /// <summary>当前正在使用的武器（供远程发射器取正确的子弹）</summary>
    public WeaponMoveSet ActiveWeapon { get; private set; }

    void Update()
    {
        // ---- 1. 预输入缓存倒计时 ----
        // ==================================================
        // 【预输入窗口锚定在"闸门打开"，不是"按下"】
        //
        // 原先是无条件倒计时：按下那一刻起 0.2 秒作废，不管手忙不忙。
        // 于是同手接招时，缓存在【招式动画演到一半】就过期了 ——
        // 而闸门（动画结束 + 后摇）还没开。输入被静默丢弃，玩家必须再按一次。
        //
        // 症状是「AA1 之后接不上 A1，把 rt 调成 0 也没用」：
        // 调 rt 是在调闸门开启的时刻，可钥匙在开门之前就自己烧掉了。
        //
        // 而长按意图（pendingHold）是个纯 bool、根本不过期，所以
        // 「A1 → AA1」一直很顺、「AA1 → A1」一直不顺 —— 两条路的待遇不一样。
        //
        // 现在对齐：闸门没开就不消耗宽恕期，只记总滞留时长兜底。
        // ==================================================
        if (hasBufferedInput)
        {
            if (IsHandReady(bufferedCmd))
            {
                bufferTimer -= Time.deltaTime;
                if (bufferTimer <= 0) hasBufferedInput = false;
            }
            else
            {
                bufferHeldWhileBusy += Time.deltaTime;

                if (bufferHeldWhileBusy > maxBufferedHoldTime)
                {
                    hasBufferedInput = false;
                    if (verboseLog)
                        Debug.Log($"[连招] {bufferedCmd} 的预输入滞留超过 {maxBufferedHoldTime:F2}s，已丢弃");
                }
            }
        }

        // ---- 2. 长按意图兑现（后摇期间被暂存的那一次）----
        TryFlushPendingHold(InputCmd.MainAttack, player != null && player.isMainAttackHeld);
        TryFlushPendingHold(InputCmd.SubAttack, player != null && player.isSubAttackHeld);

        // ---- 3. 连段超时检查 ----
        //
        // 【顺序很重要】必须排在第 4 步兑现缓存【之前】。
        //
        // 原先是先兑现、后检查，于是后摇结束的那一帧：
        //   缓存里的输入先被兑现 → currentNode 还是上一招 → 接出了第二段
        //   等超时检查跑起来才发现"连段早该重置了"，但招已经出去了。
        // 表现出来就是 Combo Window 设成 0 却依然能接出 A2。
        //
        // 比喻：闭馆时间到了，检票员却在清场之前先放了一个人进去。
        //       先清场，再检票。
        TickComboIdle();

        // ---- 4. 兑现预输入 ----
        //
        // 攻击键的预输入宽恕：后摇期间按下的那一下会被存住，
        // 后摇一结束立刻兑现，玩家不需要再按第二次。
        // （位移键早就有这套机制，攻击键原先是遗漏的。）
        //
        // 只在闸门开着时才重试 —— 后摇没结束时重试多少次结果都一样，
        // 每帧撞门只会刷出几十条日志。
        if (hasBufferedInput
            && sm.currentState != sm.comboState
            && IsHandReady(bufferedCmd))
        {
            TryAdvanceCombo();
        }

        // 【批次J 删除】TryAutoExecuteCharge
        // 原先每帧在这里检查「蓄力够没够，够了自动打出去」。
        // 现在蓄满只是把 ChargeModule 标记为「可以放了」，真正出招要等松手 ——
        // 见 OnAttackReleased。
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

        // 【修复】连段存活时长 = Recovery Time + Combo Window
        //
        // 原先两个计时是【并行】的：Combo Window 从动画结束那一刻就开始走，
        // 和 Recovery Time 同时跑。后摇 0.35 + 窗口 0.5 的话，
        // 玩家真正能接的窗口只剩 0.15 秒，而且后摇越长窗口越短 —— 很反直觉。
        //
        // 改成串行后，时间轴是：
        //     动画演完 → Recovery Time（不能出招）→ Combo Window（可接）→ 重置
        // 两个参数各管各的，调其中一个不会挤压另一个。
        float limit = currentNode.recoveryTime + currentNode.comboWindow + ComboWindowTolerance;
        if (comboIdleTimer > limit)
        {
            if (verboseLog) Debug.Log($"[连招] 闲置 {comboIdleTimer:F2}s 超过 {limit:F2}s，断档");
            ResetCombo();
        }
    }

    // ==================================================
    // 输入接口
    // ==================================================

    /// <summary>
    /// 【P1a 新增】点按 —— 打出普攻。
    ///
    /// 由 PlayerController 在"按下后 0.15 秒内松手"时调用。
    /// </summary>
    public void OnAttackTap(InputCmd cmd)
    {
        if (!AcceptsAttackInput(cmd)) return;

        hasBufferedInput = true;
        bufferedCmd = cmd;
        bufferTimer = bufferLifespan;
        bufferHeldWhileBusy = 0f;

        // 【与旧版的关键差异】这里不再判断"当前是不是在 comboState"。
        //
        // 旧版只有不在出招时才尝试推进，等于同手换手一视同仁地要等动画。
        // 新规则下换手可以抢拍，所以一律尝试 ——
        // 挡不挡得住由 IsHandReady 的后摇计时器决定，那才是真正的闸门。
        TryAdvanceCombo();
    }

    /// <summary>
    /// 【P1a 新增】长按确认 —— 直接进入蓄力，不打出普攻。
    ///
    /// 由 PlayerController 在"按住超过 0.15 秒"时调用。
    ///
    /// 旧版是"先出一发普攻，动画放完还按着才进蓄力"（效仿空洞骑士），
    /// 新规则把那一发普攻删掉了，改用这 0.15 秒的判定延迟来分离点按与长按。
    /// </summary>
    public void OnAttackHold(InputCmd cmd)
    {
        if (!AcceptsAttackInput(cmd)) return;

        // 同手后摇中不能起蓄力（普攻也不行，规则一致）。
        // 但【不能直接丢弃】—— 存下来等后摇结束再兑现，
        // 否则这一次按键就彻底消失了，玩家会觉得"按了没反应"。
        if (!IsHandReady(cmd))
        {
            pendingHold[HandIndex(cmd)] = true;
            if (verboseLog)
            {
                Debug.Log(IsHandAttacking(cmd)
                    ? $"[连招] {cmd} 出招中，长按意图已暂存"
                    : $"[连招] {cmd} 后摇中（剩 {GetHandRecoveryRemaining(cmd):F2}s），长按意图已暂存");
            }
            return;
        }

        if (chargeSystem == null)
        {
            Debug.LogError("[连招] 缺少 PlayerChargeSystem 组件，无法蓄力。", this);
            return;
        }

        // 这只手已经在蓄了就别重复开始
        if (chargeSystem.IsCharging(cmd)) return;

        hasBufferedInput = false;

        // 【P3 改动】不再 ChangeState —— 蓄力已经不是状态了。
        // 玩家仍然待在 Idle / Run / Jump 里，只是某只手攥着一个充能物。
        // 于是跳跃、跑动自然可用，另一只手也能正常打连招。
        // 【顺序很重要】先算目标等级再清连段。
        // 目标等级用的是【清零前】的深度，清零之后就取不到了。
        int target = ChargeTargetLevel;
        bool afterCombo = IsChargeAfterCombo;

        chargeSystem.BeginCharge(cmd, target, afterCombo);

        // 【规则】蓄力一开始，协助手的连段从第 1 段重新计数。
        //
        // 目标等级已经锁进模块里了，所以这里清零不影响这次蓄力 ——
        // 清的是"接下来另一只手能接第几段"。
        //
        // 例：A1 → A2（第2段）→ 长按左键蓄力（锁定 AA2）
        //     → 此时按右键打出的是 b1，不是 b3
        ResetCombo();

        if (verboseLog) Debug.Log($"[连招] {cmd} 长按确认 → 开始蓄力（目标 AA{target}）");
    }

    /// <summary>
    /// 兑现后摇期间暂存的长按意图。
    ///
    /// 松手时会清掉 —— 玩家已经放弃了，不该在几秒后突然弹出一个蓄力姿态。
    /// </summary>
    private void TryFlushPendingHold(InputCmd cmd, bool stillHeld)
    {
        int i = HandIndex(cmd);
        if (!pendingHold[i]) return;

        if (!stillHeld)
        {
            pendingHold[i] = false;
            return;
        }

        if (!IsHandReady(cmd)) return;

        pendingHold[i] = false;
        OnAttackHold(cmd);
    }

    /// <summary>
    /// 攻击输入的通用前置检查。
    ///
    /// 这里挡下的输入【一律不进缓存】—— 直接在入口丢弃，
    /// 免得闸门一开攒下的指令集体兑现，角色像自己动起来一样。
    /// </summary>
    private bool AcceptsAttackInput(InputCmd cmd)
    {
        if (cmd != InputCmd.MainAttack && cmd != InputCmd.SubAttack) return false;
        if (sm.currentState == sm.flyState) return false;
        if (sm.currentState == sm.hitState) return false;

        // 强行打出弱化蓄力后的僵直（大rt）：禁止一切输入。
        if (sm.currentState == sm.chargeStunState) return false;

        // ==================================================
        // 【规则一】弱蓄进行中 → 另一只手不能攻击。
        //
        // CD 内同时长按左右键，两只手会【各自】拿到一发弱化蓄力
        // （强弱是在各自起手那一刻分别定性的），玩家错开松手就能白拿两发。
        // 这里在源头堵住：一次只允许有一只手攥着哑弹。
        //
        // 本手不受此限 —— 正在弱蓄的那只手要能正常松手结算，
        // 而结算走的是 OnAttackReleased，根本不经过本方法。
        // 之所以还是显式排除，是为了让规则读起来就是"不许换手"。
        // ==================================================
        if (chargeSystem != null
            && chargeSystem.IsAnyWeakenedCharging
            && !chargeSystem.IsCharging(cmd))
        {
            if (verboseLog)
                Debug.Log($"[连招] 另一只手正在弱化蓄力，{cmd} 被丢弃（弱蓄期间不可换手攻击）");
            return false;
        }

        // ==================================================
        // 【规则二】弱化蓄力已打出、动画还在演 → 两只手都不能攻击。
        //
        // 这条才是真正把大rt 焊死的一条。规则一堵不住它：
        //
        //   大rt 的触发点在 OnAttackAnimationEnd —— 只有招式【自然演完】才会进。
        //   而攻击动画期间另一只手本来是完全自由的（那就是"换手抢拍"）。
        //   于是在弱化 AA1 的动画里用右手插一发进来，AA1 的动画被新招覆盖，
        //   它的结束事件永远不会触发 —— 那份大rt 凭空消失。
        //
        // 比喻：罚站的计时必须从判罚那一刻算起。要是从"走到墙角"才算，
        //       那半路被人叫走就等于没罚。
        //
        // 所以禁止的起点是【弱化招打出的那一刻】，不是【进入大rt 那一刻】。
        // 位移不受影响：动画期间照常能按冲刺/滑铲兑换预付逃逸，
        // 那条路走的是 PlayerCommandRouter，不经过本方法。
        // ==================================================
        if (sm.currentState == sm.comboState
            && sm.comboState != null
            && sm.comboState.TryGetPendingStun(out _))
        {
            if (verboseLog)
                Debug.Log($"[连招] 弱化蓄力演出中，{cmd} 被丢弃（大rt 已开始计，禁止一切攻击）");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 松开攻击键 —— 蓄力的结算点。
    ///
    /// 【P3 改动】结算搬到了这里。原先归 ChargeState.Update 管，
    /// 但蓄力已经不是状态了，得由输入层来收尾。
    ///
    /// 松手有且只有三种后果（说明书 3.4）：
    ///   蓄满   → Fired    → 打出对应等级的招式
    ///   没蓄满 → Aborted  → 什么都不放，连段清零（不会退而求其次打低一级的招）
    ///   没在蓄 → NotCharging → 只清预输入缓存，免得松手后又跑出来打一发普攻
    /// </summary>
    public void OnAttackReleased(InputCmd cmd)
    {
        if (hasBufferedInput && bufferedCmd == cmd) hasBufferedInput = false;

        if (chargeSystem == null || !chargeSystem.IsCharging(cmd)) return;

        ChargeModule module = chargeSystem.GetModule(cmd);
        int level = module != null ? module.TargetLevel : 0;

        // 【强弱在起手那一刻就定了性】这里只是把结论取出来，不重新判定。
        // 必须在 ReleaseCharge 之前读 —— Stop() 虽然刻意没清这个标记，
        // 但依赖"别人没清"是脆的。
        bool weakened = module != null && module.IsWeakened;

        ChargeReleaseResult result = chargeSystem.ReleaseCharge(cmd);

        if (result == ChargeReleaseResult.Fired)
        {
            // 等级在 ChargeModule.Begin 里就已经压到 1 了，这里取到的就是最终值
            int firedLevel = level;

            // 必须在 TryReleaseCharge【之前】置好 —— 它内部会
            // SetCurrentNode → ChangeState → ComboState.Enter，
            // 而 Enter 那一刻就要读这个标记去决定后摇和特效。
            isCurrentAttackWeakened = weakened;

            if (!TryReleaseCharge(cmd, firedLevel))
            {
                // 蓄满了但招式表没配全 —— 已经在 TryReleaseCharge 里打过诊断日志
                isCurrentAttackWeakened = false;
                ResetCombo();
                return;
            }

            // ==================================================
            // 【先占位，再延长】CD 分两步启动。
            //
            // 完整的 CD 要从收招硬直结束那一刻起算，而硬直多长要等招式
            // 演完才知道 —— 那部分在 ComboState.Exit 里补（Max 只延不缩）。
            //
            // 但如果【只】在 Exit 启动，就留下一个时间缝：
            // 蓄力招的动画还在演时，另一只手是自由的，可以开始蓄力 ——
            // 那一刻 CD 还没启动，IsChargeReady 还是 true，
            // 这次蓄力就被定性成了【正常版】。
            //
            // 症状就是「交替长按左右键无限复读 AA1/BB1」：
            // 每一发都在上一发的动画期间起手，永远踩不进 CD。
            //
            // 所以在打出的这一刻先用 delay=0 占住位，把缝焊死；
            // Exit 再用真实硬直把终点延长到位。
            // ==================================================
            if (weapons != null
                && WeaponMoveSet.TryCommandToSlot(cmd, out WeaponSlot slot))
            {
                weapons.StartChargeCooldown(slot, firedLevel, 0f);
            }

            if (verboseLog)
            {
                Debug.Log(weakened
                    ? $"[蓄力] 打出弱化版 AA{firedLevel}（起手时在 CD 内，已定性）"
                    : $"[蓄力] 正常打出 AA{firedLevel}");
            }
            return;
        }

        if (result == ChargeReleaseResult.Aborted)
        {
            // 没蓄满就松手：什么都不放，连段清零。
            // 这是"蓄力是一场赌注"的实现处 —— 不会退而求其次打出低一级的招。
            ResetCombo();
        }
    }

    // ==================================================
    // 普通连招推进
    // ==================================================

    public bool TryAdvanceCombo()
    {
        if (!hasBufferedInput) return false;

        // 【P1a 新增】同手后摇闸门。
        // 换手不查这个 —— 那正是"换手可以抢拍"的实现处。
        if (!IsHandReady(bufferedCmd))
        {
            // 到这里说明是从 OnAttackTap 直接进来的（缓存重试已经在外面挡掉了），
            // 所以一次按键最多打一条，不会刷屏
            if (verboseLog)
            {
                Debug.Log(IsHandAttacking(bufferedCmd)
                    ? $"[连招] {bufferedCmd} 出招中，已存入缓存"
                    : $"[连招] {bufferedCmd} 后摇中（剩 {GetHandRecoveryRemaining(bufferedCmd):F2}s），已存入缓存");
            }
            return false;
        }

        // 重新起手时 ResetCombo 会清掉缓存，先把指令存一份
        InputCmd savedCmd = bufferedCmd;

        // ==================================================
        // 取消权限：当前招式允许被这个键【派生】吗？
        //
        // 【只在真的处于连段中时才检查】—— 加 comboDepth > 0 这个条件。
        //
        // 取消权限的语义是"这一招能不能被打断/接续"，它约束的是【派生】。
        // 而连段深度已经归零时（蓄力招打完就是这种情况），玩家按下的那一下
        // 是一套【新连招的起手】，不是对上一招的派生 —— 拿上一招的取消权限
        // 去卡新起手是越权。
        //
        // 原先的写法还有一个更隐蔽的问题：这里是【硬 return】，
        // 不像下面 match == null 那条路会回退去"重新起手"。
        // 于是两条失败路径待遇不一致，被这道闸门挡住的输入直接消失。
        // ==================================================
        if (currentNode != null && comboDepth > 0
            && !currentNode.CanBeCanceledBy(bufferedCmd))
        {
            if (verboseLog)
                Debug.Log($"[连招] {currentNode.nodeName} 不允许被 {bufferedCmd} 派生（连段第 {comboDepth} 段）");
            return false;
        }

        CollectCandidates(bufferedCmd);

        // 普通推进只找非蓄力招 —— 蓄力招走 TryReleaseCharge
        ComboNode match = FindBestMatch(candidateBuffer, bufferedCmd, requiredChargeLevel: 0);

        // ==================================================
        // 【修复】连段打满后自动重新起手
        //
        // 症状：打完 A3（深度3）之后，按右键的 b1 也接不上。
        //
        // 原因：下一招会去找【深度4】的节点，而 b1 是深度1的起手招，
        //       匹配不上 → 玩家必须干等 comboWindow 走完、连段清零，
        //       中间那段"按什么都没反应"就是这么来的。
        //
        // 修法：找不到后续时，当作一套【新连招】重新起手。
        //       A3 → b1 立刻能接；A3 → A1 仍被同手后摇挡住 ——
        //       正是规则想要的效果。
        // ==================================================
        if (match == null && currentNode != null)
        {
            // 【诊断】走到这里说明"当前深度的下一段"一个都没匹配上。
            // 打满三段后走这条是正常的；但如果 A1 之后就走这条，
            // 说明 A2 没配对 —— 下面这条日志能直接看出是哪一环出了问题。
            if (verboseLog)
            {
                Debug.Log(
                    $"[连招诊断] {bufferedCmd} 在深度 {comboDepth + 1} 找不到后续。" +
                    $"当前招式={currentNode.nodeName}，候选数={candidateBuffer.Count}。" +
                    "候选数为 0 → 检查武器 Follow Ups 有没有配；" +
                    "候选数不为 0 → 检查这些节点的 Min/Max Combo Depth 与 Input Sequence。");
            }

            ResetCombo();

            // ResetCombo 清了缓存，重新塞回去再试一次
            hasBufferedInput = true;
            bufferedCmd = savedCmd;
            bufferTimer = bufferLifespan;
            bufferHeldWhileBusy = 0f;

            CollectCandidates(bufferedCmd);
            match = FindBestMatch(candidateBuffer, bufferedCmd, requiredChargeLevel: 0);

            if (match != null && verboseLog)
                Debug.Log($"[连招] 上一套已打满，{bufferedCmd} 作为新连招重新起手");
        }

        if (match != null)
        {
            hasBufferedInput = false;
            SetCurrentNode(match, bufferedCmd);
            sm.ChangeState(sm.comboState);
            return true;
        }

        // 【诊断】走到这里 = 闸门开着、也回退过了，但一个招式都没匹配上。
        // 玩家的表现是"按了完全没反应"，而在此之前这条路是静默的 ——
        // 排查时只能靠猜。现在它会点名候选数，直接指向是没配还是配错了。
        if (verboseLog)
        {
            string w = weapons != null && weapons.GetWeaponForCommand(bufferedCmd) != null
                ? weapons.GetWeaponForCommand(bufferedCmd).displayName
                : "无武器";

            Debug.Log(
                $"[连招诊断] {bufferedCmd} 无任何匹配（武器={w}，连段深度={comboDepth}，" +
                $"候选数={candidateBuffer.Count}）。" +
                (candidateBuffer.Count == 0
                    ? "候选数为 0 → 该深度下这把武器的 Openers/Follow Ups 里没有可用招式。"
                    : "候选数不为 0 → 检查这些节点的 Input Sequence 末位、Cast Condition 与 Required State。"));
        }

        return false;
    }

    // ==================================================
    // 蓄力释放
    // ==================================================

    /// <summary>
    /// 松手蓄满时由 OnAttackReleased 调用。
    /// 在【等级 <= level】的蓄力招里挑最高的那一个打出去。
    /// </summary>
    /// <returns>是否成功打出了蓄力招</returns>
    public bool TryReleaseCharge(InputCmd cmd, int level)
    {
        if (level <= 0) return false;

        // 蓄力招不受「起手 / 接续」划分限制，两个列表一起找
        CollectCandidates(cmd, alwaysIncludeOpeners: true);

        ComboNode match = FindBestMatch(candidateBuffer, cmd, requiredChargeLevel: level);

        if (match == null)
        {
            if (verboseLog)
            {
                Debug.Log(
                    $"[连招] 蓄满 AA{level} 但找不到对应招式。候选数={candidateBuffer.Count}。\n" +
                    "请检查：① 该武器的 Openers/Follow Ups 里有没有这一级的蓄力招；" +
                    "② 那个节点的 Charge Level 是否等于 " + level + "；" +
                    "③ Is Charge Skill 有没有勾上；" +
                    "④ Input Sequence 末位是不是对应的攻击键。");
            }
            return false;
        }

        hasBufferedInput = false;
        SetCurrentNode(match, cmd);
        sm.ChangeState(sm.comboState);

        if (verboseLog) Debug.Log($"[连招] 蓄力释放：{match.nodeName} (等级 {match.chargeLevel}/{level})");
        return true;
    }

    // 【已删除】TryThrust —— 批次L 的「攻击中单按 shift = 突刺」。
    // P1b 把规则改成了「攻击动画中按 Shift 一律位移并打断连段」，
    // 路由器从此不再调用它，说明书 4.2 的表里也没有突刺这一项了。

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
    /// <param name="cmd">
    /// 触发键。既用来匹配 inputSequence 的末位，也用来决定从哪把武器取候选。
    /// </param>
    /// <param name="alwaysIncludeOpeners">
    /// 【蓄力专用】连招还没开始时也把 Follow Ups 一起纳入候选。
    ///
    /// 起因：原地起手蓄力时 currentNode 是 null，
    /// 引擎只会去翻 Openers —— 而蓄力招一般都配在 Follow Ups 里，
    /// 于是"原地蓄满却什么都放不出"。
    ///
    /// 根本原因是「起手招 / 接续招」这个划分是为【普攻】设计的，
    /// 用来表达"第几段能出什么"。而蓄力招的等级现在由连段深度决定，
    /// 压根不需要靠列表位置区分，被这个划分卡住没有道理。
    ///
    /// 比喻：菜单分了"前菜"和"主菜"两页，但饮料两页都没有。
    ///       不该让客人去前菜页找可乐，而该认识到饮料不属于这个分类。
    /// </param>
    private void CollectCandidates(InputCmd cmd, bool alwaysIncludeOpeners = false)
    {
        candidateBuffer.Clear();

        WeaponMoveSet weapon = weapons != null ? weapons.GetWeaponForCommand(cmd) : null;

        // ==================================================
        // 【判"有没有连段"要看深度，不能只看指针】
        //
        // 蓄力招打出后 SetCurrentNode 会把 comboDepth 归零，但 currentNode
        // 仍然指着那个蓄力招 —— 状态自相矛盾：深度说"没有连段"，指针说"连段进行中"。
        //
        // 原先这里只看指针，于是走接续分支去找【深度 0+1 = 1】的招，
        // 而接续分支只翻 childNodes 和 followUps，【不翻 openers】——
        // A1 恰恰是配在 openers 里的起手招，于是一个候选都匹配不上。
        //
        // 症状：AA1 之后同手接不出 A1，必须干等连招指针被 TickComboIdle 清掉
        // （所以把 recoveryTime 调成负数会"变顺" —— 那是在缩短指针存活时长，
        //   跟后摇毫无关系，BeginHandRecovery 里负数本来就被 Max(0) 钳掉了）。
        //
        // 为什么只有近战犯病：接续分支翻的是 followUps，各武器配置不同 ——
        // 远程的 followUps 里有 B1（深度 1 能匹配），近战的只有 A2/A3。
        // 换手同理，翻的是另一把武器的 followUps。
        // ==================================================
        if (currentNode == null || comboDepth <= 0)
        {
            int depth = 1;

            if (weapon != null)
            {
                AddCandidates(weapon.openers, depth);

                // 蓄力：两个列表一起找
                if (alwaysIncludeOpeners) AddCandidates(weapon.followUps, depth);
            }

            // 【回退条件与下方接续分支对齐】必须加上 weapon == null。
            //
            // 原先这里是无条件回退：只要候选为空就去翻 rootNodes。
            // 而接续分支要求"候选为空【且】没装武器"才回退 —— 两条路待遇不一致。
            //
            // 后果是：装了武器、但某一段的招式没配好时，
            //   起手 → 悄悄从 rootNodes 里捞一个出来顶上，看起来"能打"
            //   接续 → 老老实实报诊断日志
            // 于是"招式没配"这个问题在起手时被静默掩盖，
            // 而 rootNodes 本来就只是「完全没装武器」时的兜底，不该越权顶班。
            if (candidateBuffer.Count == 0 && weapon == null)
                AddCandidates(rootNodes, depth);   // 回退：没装武器就用旧配置

            return;
        }

        int nextDepth = comboDepth + 1;

        AddCandidates(currentNode.childNodes, nextDepth);          // 同武器固定衔接
        if (weapon != null)
        {
            AddCandidates(weapon.followUps, nextDepth);            // 跨武器接续

            // 蓄力时连起手列表也一并纳入，语义上更完整
            if (alwaysIncludeOpeners) AddCandidates(weapon.openers, nextDepth);
        }

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

        // 【规则】蓄力打出后连段重新计数。
        //
        // 蓄力招不推进段数，而是把计数归零 ——
        // 所以 A1→A2→蓄出AA2 之后再长按，是从 AA1 重新开始，
        // 而不是接着蓄 AA3。想要 AA3 就必须重新打满三段普攻。
        if (node.isChargeSkill) comboDepth = 0;
        else
        {
            comboDepth++;

            // 普通招式一定不是缩水蓄力。放在这里清，是因为蓄力那条路
            // 会在 TryReleaseCharge 之前先置位，不能被后面覆盖掉。
            isCurrentAttackWeakened = false;
        }
        comboIdleTimer = 0f;
        LastTriggerCmd = triggerCmd;

        // 出招即锁本手，直到招式结束才开始算后摇。
        // 另一只手不受影响，这就是换手抢拍的基础。
        MarkHandBusy(triggerCmd);

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

    /// <summary>
    /// 清空连段。
    ///
    /// 【注意】不清手部后摇 —— 连段断了不代表手就闲下来了。
    /// 比如蓄力失败时连段归零，但那只手的后摇仍然该走完。
    /// </summary>
    public void ResetCombo()
    {
        currentNode = null;
        comboDepth = 0;
        comboIdleTimer = 0f;
        hasBufferedInput = false;
        ActiveWeapon = null;
        isCurrentAttackWeakened = false;
    }

    /// <summary>当前招式能否被位移动作打断。供 PlayerCommandRouter 查询</summary>
    public bool CanCurrentNodeBeCanceledByMovement()
        => currentNode == null || currentNode.CanBeCanceledByMovement;
}