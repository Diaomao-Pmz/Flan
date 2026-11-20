using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

/// <summary>
/// 【连招匹配引擎】
/// </summary>
// AttackTimelineRunner 常驻在玩家 prefab 上；RequireComponent 保证新挂 ComboInputBuffer 时自动带上它。
[RequireComponent(typeof(AttackTimelineRunner))]
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

    [Header("后摇")]
    [Tooltip(
        "后摇本身不锁移动（后摇允许移动）。\n\n" +
        "这个开关只管一种情况：动画已经被别的招顶掉、但判定/连射还在跑的时候，\n" +
        "是否继续按「判定期间禁止移动」锁住走动与跳跃。默认开启。")]
    [UnityEngine.Serialization.FormerlySerializedAs("lockMovementDuringRecovery")]
    public bool lockMovementWhileHitActive = true;

    // ==========================================================
    // 【实验：平a/蓄力重做】
    // ==========================================================
    [Header("实验：平a / 蓄力重做")]
    [Tooltip(
        "【平a后接蓄力】的起步时间（秒）。连段进行中（包括原地 A1/B1 起手之后）长按，\n" +
        "按住满这么久起步等级就生效；不满就松手 = 什么都不放。\n\n" +
        "连段为 0 时原地起手不用这个值：要按住满武器上的 Charge Time Lv1 才到 1 级。")]
    [Min(0f)]
    public float chargeStartHoldTime = 0.3f;

    [Tooltip(
        "飞行中松手放出蓄力招时，人会先变回地面状态把这一招打出来。\n" +
        "勾上（默认）= 这一招进入后摇阶段后自动变回飞行状态（MP 耗尽则不回）。\n" +
        "取消勾选 = 不自动回，放完就往下掉，想继续飞要自己再按一次飞行键。")]
    public bool autoResumeFlightAfterCharge = true;

    /// <summary>currentNode 是飞行攻击登记的（不是地面连招 / 蓄力招）</summary>
    private bool currentNodeIsFly;

    /// <summary>平a 段数上限。打到这一段后再打回到第 1 段（R3）</summary>
    public const int MaxComboDepth = 3;

    /// <summary>下一下平a是第几段：1→2→3→1 循环</summary>
    public static int NextDepth(int depth) => (depth <= 0 || depth >= MaxComboDepth) ? 1 : depth + 1;

    /// <summary>最近一次地面出招的攻击实例（当前动画的主人）</summary>
    public AttackInstance CurrentInstance { get; private set; }

    /// <summary>当前这一招自己所处的阶段（冲刺/滑铲判断"打断的是不是前摇"用）</summary>
    public AttackPhase CurrentAttackPhase
        => CurrentInstance != null ? CurrentInstance.Phase : AttackPhase.None;

    /// <summary>
    /// 移动规则用的阶段（R1）。
    /// 只要有任意一份地面判定还在跑（包括动画已被顶掉的旧招）就算「判定中」，否则看当前这一招。
    /// </summary>
    public AttackPhase MovementPhase
    {
        get
        {
            if (timeline != null && timeline.AnyGroundActive()) return AttackPhase.Active;
            return CurrentAttackPhase;
        }
    }

    private AttackTimelineRunner timeline;
    private AttackInstance flyInstance;

    /// <summary>当前飞行攻击的攻击实例（FlyCombatState 用它给动画调速、算这一招演多久）</summary>
    public AttackInstance FlyInstance => flyInstance;

    /// <summary>连段到这个时刻还没有新的平a就归零（绝对时间）。蓄力输入不会推迟它（R3）</summary>
    private float comboExpireTime;

    /// <summary>连段窗口还剩多少秒（连段为 0 时是 0）。只供调试面板 / UI 显示</summary>
    public float ComboWindowRemaining
        => comboDepth > 0 ? Mathf.Max(0f, comboExpireTime - Time.time) : 0f;

    // ---- 双蓄力（R5）----
    private struct PendingDual { public bool active; public InputCmd cmd; public int level; public float expire; }
    private PendingDual pendingDual;      // 判定方式 A：挂起等另一只手的第一发

    private struct LastSingle
    {
        public bool valid; public InputCmd cmd; public int level; public float time;
        public AttackInstance instance; public bool otherWasValid;
    }
    private LastSingle lastSingle;        // 判定方式 B：刚打出去的那一发单蓄力

    [Header("Debug")]
    public bool verboseLog = false;

    // ---- 预输入缓存 ----
    private bool hasBufferedInput = false;
    private InputCmd bufferedCmd;
    private float bufferTimer = 0f;

    /// <summary>缓存在「闸门没开」状态下已经滞留了多久。用于兜底丢弃，见 maxBufferedHoldTime</summary>
    private float bufferHeldWhileBusy = 0f;

    private PlayerStateMachine sm;
    private PlayerController player;
    private PlayerState state;
    private WeaponLoadout weapons;
    private PlayerChargeSystem chargeSystem;

    // 复用列表，避免每帧 new 产生 GC
    private readonly List<ComboNode> candidateBuffer = new List<ComboNode>(16);

    // ==================================================
    // 【P1a 新增】每只手一个独立的后摇计时器
    // ==================================================
    private readonly float[] handBusyUntil = new float[2];

    /// <summary>每只手当前这一招的动画预计演到什么时候（之前算"出招中"，之后算"后摇中"）</summary>
    private readonly float[] handAnimEnd = new float[2];

    /// <summary>每只手最近一次出招的编号</summary>
    private readonly int[] handAttackId = new int[2];
    private int attackIdCounter;

    // ---- 「刚放完蓄力」按手判断 ----
    // 出招流水号：每打出一招 +1。记下"最近一次平a"和"每只手最近一次放蓄力"各是第几号，
    // 比大小就知道"这只手放完蓄力之后，有没有人打过平a"。
    // （原先看的是两只手共用的 currentNode：另一只手一放蓄力，这只手刚打的平a就被忘掉了）
    private int actionSerial;
    private int lastNormalAttackSerial;
    private readonly int[] lastChargeReleaseSerial = new int[2];

    /// <summary>这只手刚放完蓄力，之后两只手都还没打过平a</summary>
    private bool HandJustReleasedCharge(InputCmd cmd)
        => lastChargeReleaseSerial[HandIndex(cmd)] > lastNormalAttackSerial;

    /// <summary>
    /// 后摇期间按下的长按意图，存着等后摇结束再兑现（松手则作废）。
    /// </summary>
    private readonly bool[] pendingHold = new bool[2];

    /// <summary>暂存的长按意图对应的起步等级（长按判定成立那一刻的连段深度）</summary>
    private readonly int[] pendingHoldLevel = new int[2];

    /// <summary>暂存的长按意图是不是接在平a后面（决定起步时间是 0.3s 还是武器的 Lv1 阈值）</summary>
    private readonly bool[] pendingHoldAfterCombo = new bool[2];

    /// <summary>这只手当前这次蓄力是不是「平a后接蓄力」。只在该手蓄力中有意义</summary>
    private readonly bool[] chargeIsAfterCombo = new bool[2];

    private static int HandIndex(InputCmd cmd)
        => cmd == InputCmd.SubAttack ? 1 : 0;

    private static InputCmd OtherHand(InputCmd cmd)
        => cmd == InputCmd.SubAttack ? InputCmd.MainAttack : InputCmd.SubAttack;

    /// <summary>这只手现在能不能出招（招式演完且后摇结束了吗）</summary>
    public bool IsHandReady(InputCmd cmd)
        => Time.time >= handBusyUntil[HandIndex(cmd)];

    /// <summary>这只手正在出招中（招式动画还没到自然结束的时刻）</summary>
    public bool IsHandAttacking(InputCmd cmd)
        => !IsHandReady(cmd) && Time.time < handAnimEnd[HandIndex(cmd)];

    /// <summary>
    /// 该手剩余后摇秒数。供 UI 显示。
    /// 出招中时返回 -1，调用方据此区分两种忙碌。
    /// </summary>
    public float GetHandRecoveryRemaining(InputCmd cmd)
    {
        if (IsHandAttacking(cmd)) return -1f;
        return Mathf.Max(0f, handBusyUntil[HandIndex(cmd)] - Time.time);
    }

    /// <summary>
    /// 【实验改动】出招即把这只手锁到这一招的总时长（前摇 + 攻击 + 后摇）结束。
    /// </summary>
    /// <returns>本次出招的编号</returns>
    private int MarkHandBusy(InputCmd cmd, float animDuration, float recovery)
    {
        int i = HandIndex(cmd);
        handAnimEnd[i] = Time.time + Mathf.Max(0f, animDuration);
        handBusyUntil[i] = handAnimEnd[i] + Mathf.Max(0f, recovery);
        handAttackId[i] = ++attackIdCounter;
        return handAttackId[i];
    }

    /// <summary>取某只手当前的出招编号</summary>
    public int GetHandAttackId(InputCmd cmd) => handAttackId[HandIndex(cmd)];

    /// <summary>从现在起这只手只剩 seconds 秒后摇</summary>
    public void BeginHandRecovery(InputCmd cmd, float seconds, int attackId = -1)
    {
        int i = HandIndex(cmd);

        // 核对编号：这只手已经开始了更新的一次出招，就别拿旧招的后摇去覆盖它
        if (attackId >= 0 && attackId != handAttackId[i])
        {
            if (verboseLog)
                PlayerLog.Info($"[连招] {cmd} 的旧招后摇被忽略（已有更新的出招）");
            return;
        }

        handAnimEnd[i] = Time.time;
        handBusyUntil[i] = Time.time + Mathf.Max(0f, seconds);

        if (verboseLog)
            PlayerLog.Info($"[连招] {cmd} 进入后摇 {seconds:F2}s");
    }

    /// <summary>立刻解锁一只手（招式作废 / 被冲刺滑铲打断）</summary>
    private void FreeHand(InputCmd cmd)
    {
        int i = HandIndex(cmd);
        handAnimEnd[i] = 0f;
        handBusyUntil[i] = 0f;
    }

    /// <summary>
    /// 任意一只手正忙（出招中或后摇中）。
    /// </summary>
    public bool IsAnyHandBusy()
        => !IsHandReady(InputCmd.MainAttack) || !IsHandReady(InputCmd.SubAttack);

    /// <summary>
    /// 【实验后含义已变】攻击是否仍然锁住移动。Idle/Run/路由器查这个。
    /// </summary>
    public bool IsMovementLockedByRecovery()
        => lockMovementWhileHitActive && timeline != null && timeline.AnyGroundActive();

    // ==================================================
    // 【蓄力准备窗口】攻击键已判定为长按，但人还在攻击动画 / 后摇里、蓄力还没真正放开手脚
    // ==================================================

    /// <summary>
    /// 本次蓄力是在攻击动画 / 后摇里开始的（换手情况：A1 后马上按住右键，右手立刻开蓄，人还在 A1 里）。
    /// 蓄力开始那一刻记下，窗口一过或蓄力结束就清 —— 这样蓄力中途另一只手再出招，仍按普通攻击规则锁移动。
    /// </summary>
    private bool chargeBeganInsideAttack;

    /// <summary>有手正在"按住准备蓄力"：长按已判定、在等后摇结束，或蓄力是在攻击里开始的</summary>
    public bool IsHoldingForCharge
        => pendingHold[0] || pendingHold[1]
           || (chargeBeganInsideAttack && chargeSystem != null && chargeSystem.IsAnyCharging);

    /// <summary>处于蓄力准备窗口：按住准备蓄力，且人还在攻击动画或后摇里</summary>
    public bool IsInPreChargeWindow
        => IsHoldingForCharge && (sm.currentState == sm.comboState || IsAnyHandBusy());

    /// <summary>清空两只手的后摇。受击、死亡、切场景时用</summary>
    public void ClearHandRecovery()
    {
        handBusyUntil[0] = 0f;
        handBusyUntil[1] = 0f;
        handAnimEnd[0] = 0f;
        handAnimEnd[1] = 0f;
        pendingHold[0] = false;
        pendingHold[1] = false;

        // 受击：所有还在跑的攻击逻辑一起掐断，双蓄力的等待状态也作废
        timeline?.VoidAll();
        pendingDual.active = false;
        lastSingle.valid = false;
    }

    void Awake()
    {
        sm = GetComponent<PlayerStateMachine>();
        player = GetComponent<PlayerController>();
        state = GetComponent<PlayerState>();
        weapons = GetComponent<WeaponLoadout>();
        chargeSystem = GetComponent<PlayerChargeSystem>();

        // 【正式版整改 批次A】时间表执行器常驻在玩家 prefab 上，不在运行时 AddComponent
        // 症状：试验版在这里 AddComponent 补挂，违反"组件常驻、禁止运行时增删"
        // 根因：AttackTimelineRunner 当时没有独立文件，编辑器里挂不上，只能代码补挂；
        //       现已拆成 AttackTimelineRunner.cs 并挂到 prefab，这里只取引用，缺失时报错
        timeline = GetComponent<AttackTimelineRunner>();
        if (timeline == null)
        {
            PlayerLog.Error(
                "[连招] 玩家身上缺少 AttackTimelineRunner 组件，攻击 / 冲刺 / 滑铲会报空引用。\n" +
                "请在玩家 prefab 上添加：Add Component → Attack Timeline Runner，然后 Apply。", this);
        }
    }

    private float ComboWindowTolerance
        => state != null ? state.stats.comboWindowTolerance.Value : 0f;

    /// <summary>当前正在使用的武器（供远程发射器取正确的子弹）</summary>
    public WeaponMoveSet ActiveWeapon { get; private set; }

    void Update()
    {
        // ---- 0. 蓄力准备窗口收尾：攻击动画和后摇都走完了，或蓄力已经结束 ----
        if (chargeBeganInsideAttack)
        {
            bool stillInAttack = sm.currentState == sm.comboState || IsAnyHandBusy();
            bool stillCharging = chargeSystem != null && chargeSystem.IsAnyCharging;
            if (!stillInAttack || !stillCharging) chargeBeganInsideAttack = false;
        }

        // ---- 1. 预输入缓存倒计时 ----
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
                        PlayerLog.Info($"[连招] {bufferedCmd} 的预输入滞留超过 {maxBufferedHoldTime:F2}s，已丢弃");
                }
            }
        }

        // ---- 1.5 双蓄力判定方式 A：挂起的第一发等不到第二发 → 按单蓄力打出 ----
        if (pendingDual.active && Time.time >= pendingDual.expire)
        {
            PendingDual p = pendingDual;
            pendingDual.active = false;
            FireSingle(p.cmd, p.level);
        }

        // ---- 2. 长按意图兑现（后摇期间被暂存的那一次）----
        TryFlushPendingHold(InputCmd.MainAttack, player != null && player.isMainAttackHeld);
        TryFlushPendingHold(InputCmd.SubAttack, player != null && player.isSubAttackHeld);

        // ---- 3. 连段超时检查 ----
        TickComboIdle();

        // ---- 4. 兑现预输入 ----
        if (sm.IsFlying) hasBufferedInput = false;

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

        // 攻击动画还在演 / 飞行攻击中：不判超时
        if (sm.currentState == sm.comboState || sm.currentState == sm.flyCombatState) return;

        // 冲刺 / 滑铲永不打断连段（R1）：这段时间不计入窗口
        if (sm.currentState == sm.dashState || sm.currentState == sm.slideState)
        {
            comboExpireTime += Time.deltaTime;
            return;
        }

        // 【实验改动 · R3】连段窗口 = 这一招的总时长（前摇 + 攻击 + 后摇）走完 + Combo Window，
        // 用绝对时刻记（出招时算好）。
        // 窗口内没有新的【平a】→ 归零；蓄力输入不延长窗口 —— 正在蓄的力不受影响，继续蓄。
        if (Time.time > comboExpireTime)
        {
            if (verboseLog) PlayerLog.Info("[连招] 连段窗口到期，段数归零");
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

        // 【实验改动】飞行中的点按 = 一发飞行攻击（原先是"按下那一刻"就出，且按住会连发）。
        // 飞行中现在可以蓄力，所以和地面一样先分辨点按 / 长按：点按出飞行攻击，长按蓄力。
        if (sm.IsFlying)
        {
            sm.flyCombatState.QueueAttack(cmd);

            // 已经在交战模式里 → 排上队就行，演完当前这招自然轮到它
            if (sm.currentState == sm.flyState) sm.ChangeState(sm.flyCombatState);
            return;
        }

        hasBufferedInput = true;
        bufferedCmd = cmd;
        bufferTimer = bufferLifespan;
        bufferHeldWhileBusy = 0f;

        // 【与旧版的关键差异】这里不再判断"当前是不是在 comboState"。
        TryAdvanceCombo();
    }

    /// <summary>
    /// 【P1a 新增】长按确认 —— 直接进入蓄力，不打出普攻。
    /// </summary>
    public void OnAttackHold(InputCmd cmd)
    {
        if (!AcceptsAttackInput(cmd)) return;

        if (chargeSystem == null)
        {
            Debug.LogError("[连招] 缺少 PlayerChargeSystem 组件，无法蓄力。", this);
            return;
        }

        // 这只手已经在蓄了就别重复开始
        if (chargeSystem.IsCharging(cmd)) return;

        int i = HandIndex(cmd);

        // 这只手刚放完蓄力（之后还没人打过平a）→ 这次长按按"原地起手"从头蓄（见下）。
        // 只看这只手自己：另一只手放蓄力不影响这只手"平a后接蓄力"的判定
        bool afterChargeRelease = HandJustReleasedCharge(cmd);

        // 【2026-11-16 改回】平a后【换手】也可以蓄力了：
        // 长按另一只手不再当作点按，而是直接起蓄，起步规则和同手一样 ——
        // 0.3s 到当前连段深度对应的等级，默认锁在这一级，按侧键解锁才升段。
        // （R2 的"只能接同手同深度"已取消，原来的转点按逻辑删掉了。）

        // 起步等级 = 连段深度（A2 后 → 2 级）；连段为 0（原地 / 已超时）→ 1 级。
        // 在长按判定成立的这一刻就定下来，之后连段窗口到期也不影响这次蓄力。
        bool afterCombo = comboDepth > 0 && !afterChargeRelease;
        int startLevel = afterCombo ? Mathf.Clamp(comboDepth, 1, WeaponMoveSet.MaxChargeLevel) : 1;

        // 同手后摇中不能起蓄力（普攻也不行，规则一致）。
        // 存下来等后摇结束再兑现，否则这一次按键就彻底消失了。
        if (!IsHandReady(cmd))
        {
            pendingHold[i] = true;
            pendingHoldLevel[i] = startLevel;
            pendingHoldAfterCombo[i] = afterCombo;

            if (verboseLog)
            {
                PlayerLog.Info(IsHandAttacking(cmd)
                    ? $"[连招] {cmd} 出招中，长按意图已暂存（起步 {startLevel} 级）"
                    : $"[连招] {cmd} 后摇中（剩 {GetHandRecoveryRemaining(cmd):F2}s），长按意图已暂存（起步 {startLevel} 级）");
            }
            return;
        }

        BeginChargeNow(cmd, startLevel, afterCombo);
    }

    // 【2026-11-17 删除】ShouldTurnSecondHoldIntoTap：「平a后两只手一起长按，只有先起蓄的那只手蓄力，后一只手改按平a」。
    // 这条规则会让平a之后永远打不出双蓄，还会把第二只手的长按变成一发平a、改掉段数。
    // 现在平a后两只手都能蓄，起步等级都按当前段数。双蓄规则以后再定。

    private void BeginChargeNow(InputCmd cmd, int startLevel, bool afterCombo)
    {
        if (chargeSystem == null || chargeSystem.IsCharging(cmd)) return;

        chargeIsAfterCombo[HandIndex(cmd)] = afterCombo;

        if (hasBufferedInput && bufferedCmd == cmd) hasBufferedInput = false;

        // 换手蓄力时人可能还在攻击动画 / 后摇里 —— 记下来，让蓄力准备窗口把这段也算进去
        chargeBeganInsideAttack = sm.currentState == sm.comboState || IsAnyHandBusy();

        // 起步时间：
        //   平a后接蓄力（含原地 A1/B1 起手之后）→ chargeStartHoldTime（0.3s）就进入对应等级
        //   连段为 0 的原地起手                 → 要按住满武器的 Lv1 阈值（1s）才到 1 级
        float startHold = chargeStartHoldTime;
        if (!afterCombo)
        {
            WeaponMoveSet w = weapons != null ? weapons.GetWeaponForCommand(cmd) : null;
            startHold = w != null ? w.chargeTimeLv1 : 1f;
        }

        chargeSystem.BeginCharge(cmd, startLevel, startHold, afterCombo);

        // 【实验改动 · R6】蓄力开始不再清连段 —— 一只手蓄着，另一只手的平a段数照常往下数。

        if (verboseLog)
            PlayerLog.Info($"[连招] {cmd} 长按确认 → 开始蓄力（起步 {startLevel} 级，起步时间 {startHold:F2}s，{(afterCombo ? "平a后" : "原地起手")}）");
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

        if (!AcceptsAttackInput(cmd)) return;

        BeginChargeNow(cmd, pendingHoldLevel[i], pendingHoldAfterCombo[i]);
    }

    /// <summary>
    /// 攻击输入的通用前置检查。
    /// </summary>
    private bool AcceptsAttackInput(InputCmd cmd)
    {
        if (cmd != InputCmd.MainAttack && cmd != InputCmd.SubAttack) return false;

        // 飞行中不整体拒收：点按在 OnAttackTap 里转成飞行攻击，长按照常蓄力。
        if (sm.currentState == sm.hitState) return false;

        return true;
    }

    /// <summary>
    /// 松开攻击键 —— 蓄力的结算点。
    /// </summary>
    public void OnAttackReleased(InputCmd cmd)
    {
        if (hasBufferedInput && bufferedCmd == cmd) hasBufferedInput = false;

        if (chargeSystem == null || !chargeSystem.IsCharging(cmd)) return;

        ChargeModule module = chargeSystem.GetModule(cmd);

        // 必须在 ReleaseCharge 之前读
        int level = module != null ? module.CurrentLevel : 0;

        ChargeReleaseResult result = chargeSystem.ReleaseCharge(cmd);

        // 【实验改动】没过起步时间就松手（Aborted）：什么都不放，连段也不动。
        if (result != ChargeReleaseResult.Fired) return;

        HandleChargeFired(cmd, level);
    }

    // ==================================================
    // 【实验 · R5】单蓄力 / 双蓄力强化招的分流
    // ==================================================

    /// <summary>
    /// 一只手的有效蓄力松手了。
    /// </summary>
    private void HandleChargeFired(InputCmd cmd, int level)
    {
        InputCmd other = OtherHand(cmd);
        bool modeA = weapons != null && weapons.dualChargeMode == DualChargeMode.A_HoldFirst;
        float window = weapons != null ? weapons.dualChargeWindow : 0.2f;

        // ---- A：另一只手的第一发正挂着等我 ----
        if (pendingDual.active && pendingDual.cmd == other)
        {
            PendingDual p = pendingDual;
            pendingDual.active = false;

            DualChargeRecipe recipe = FindDualRecipe(p.cmd, p.level, cmd, level);
            if (recipe != null)
            {
                FireDual(recipe, cmd, null);
                return;
            }

            // 查不到配方 → 两发单蓄力先后打出
            FireSingle(p.cmd, p.level);
            FireSingle(cmd, level);
            return;
        }

        // ---- B：另一只手刚在窗口内放过一发（当时我也在有效蓄力）→ 撤掉它换强化招 ----
        if (!modeA && lastSingle.valid && lastSingle.cmd == other && lastSingle.otherWasValid
            && Time.time - lastSingle.time <= window)
        {
            DualChargeRecipe recipe = FindDualRecipe(lastSingle.cmd, lastSingle.level, cmd, level);
            if (recipe != null)
            {
                AttackInstance first = lastSingle.instance;
                lastSingle.valid = false;

                // 第一发：动画（由强化招顶掉）、判定、子弹、特效全停
                timeline.KillAll(first);

                FireDual(recipe, cmd, first);
                return;
            }
        }

        // ---- A：另一只手也在有效蓄力 → 先挂起，等它松手 ----
        if (modeA && chargeSystem != null && chargeSystem.IsValidCharging(other))
        {
            pendingDual = new PendingDual { active = true, cmd = cmd, level = level, expire = Time.time + window };
            if (verboseLog) PlayerLog.Info($"[双蓄] {cmd} {level} 级先挂起 {window:F2}s，等另一只手松手");
            return;
        }

        FireSingle(cmd, level);
    }

    /// <summary>打出一发单蓄力，并记下来供判定方式 B 回头替换</summary>
    private bool FireSingle(InputCmd cmd, int level)
    {
        bool otherValid = chargeSystem != null && chargeSystem.IsValidCharging(OtherHand(cmd));

        // 蓄到了但招式表没配全 —— TryReleaseCharge 里已经打过诊断日志；什么都不放，连段不动
        if (!TryReleaseCharge(cmd, level)) return false;

        lastSingle = new LastSingle
        {
            valid = true, cmd = cmd, level = level, time = Time.time,
            instance = CurrentInstance, otherWasValid = otherValid,
        };

        if (verboseLog) PlayerLog.Info($"[蓄力] {cmd} 打出 {level} 级单蓄力，段数 = {comboDepth}");
        return true;
    }

    private DualChargeRecipe FindDualRecipe(InputCmd cmdA, int levelA, InputCmd cmdB, int levelB)
    {
        if (weapons == null) return null;

        int mainLevel = cmdA == InputCmd.MainAttack ? levelA : levelB;
        int subLevel = cmdA == InputCmd.MainAttack ? levelB : levelA;
        return weapons.FindDualRecipe(mainLevel, subLevel);
    }

    /// <summary>打出双蓄力强化招。打出后连段归零（R7）</summary>
    /// <param name="replaced">判定方式 B 下被撤掉的第一发（没有则 null）</param>
    private void FireDual(DualChargeRecipe recipe, InputCmd lastReleasedCmd, AttackInstance replaced)
    {
        WeaponMoveSet weapon = null;
        if (weapons != null)
        {
            switch (recipe.weaponSource)
            {
                case DualWeaponSource.Main: weapon = weapons.mainWeapon; break;
                case DualWeaponSource.Sub: weapon = weapons.subWeapon; break;
                default:
                    if (weapons.mainWeapon != null && weapons.mainWeapon.IsRanged) weapon = weapons.mainWeapon;
                    else if (weapons.subWeapon != null && weapons.subWeapon.IsRanged) weapon = weapons.subWeapon;
                    else weapon = weapons.GetWeaponForCommand(lastReleasedCmd);
                    break;
            }
        }

        hasBufferedInput = false;
        lastSingle.valid = false;

        SetCurrentNode(recipe.result, lastReleasedCmd, dual: true, weaponOverride: weapon);

        // 回退快照取"第一发之前"的连段状态：强化招的前摇被冲刺打断时，退回到两发都没放的时候
        if (replaced != null && CurrentInstance != null)
        {
            CurrentInstance.prevNode = replaced.prevNode;
            CurrentInstance.prevDepth = replaced.prevDepth;
            CurrentInstance.prevCmd = replaced.prevCmd;
            CurrentInstance.prevWeapon = replaced.prevWeapon;
            CurrentInstance.prevExpire = replaced.prevExpire;
        }

        sm.ChangeState(sm.comboState);

        if (verboseLog) PlayerLog.Info($"[双蓄] 强化招：{recipe.result.nodeName}（配方 {recipe.name}），连段归零");
    }

    // ==================================================
    // 飞行攻击
    // ==================================================

    /// <summary>
    /// 飞行攻击出手：把这只手武器上配的「飞行攻击」登记为当前招式。
    /// </summary>
    /// <returns>本次出的招；武器或招式没配时返回 null</returns>
    public ComboNode BeginFlyAttack(InputCmd cmd)
    {
        WeaponMoveSet weapon = weapons != null ? weapons.GetWeaponForCommand(cmd) : null;
        ComboNode node = weapon != null ? weapon.flyAttack : null;

        if (node == null)
        {
            Debug.LogWarning(
                weapon == null
                    ? $"[飞行攻击] {cmd} 没有装备武器，无法出招。"
                    : $"[飞行攻击] 武器「{weapon.displayName}」没有配置 Fly Attack，无法出招。" +
                      "请在武器资产的「飞行攻击」栏位拖入对应招式（主武器 A1 / 副武器 B1）。",
                this);
            return null;
        }

        hasBufferedInput = false;
        pendingHold[HandIndex(cmd)] = false;

        currentNode = node;
        comboDepth = 0;
        LastTriggerCmd = cmd;
        ActiveWeapon = weapon;

        // 【实验改动】判定/子弹/特效改由时间表执行，飞行攻击也要开一份攻击实例。
        // 飞行攻击一次只有一份：上一份还没走完就掐掉（和旧版"新招顶掉旧判定"一致）
        if (flyInstance != null) timeline.Void(flyInstance);
        flyInstance = timeline.Begin(node, weapon, cmd, isFly: true);
        currentNodeIsFly = true;

        if (verboseLog) PlayerLog.Info($"[飞行攻击] {node.nodeName}（{weapon.displayName}）");
        return node;
    }

    /// <summary>离开飞行战斗时调用：清掉登记的招式，落地后连段从第 1 段开始</summary>
    public void EndFlyAttack()
    {
        if (flyInstance != null)
        {
            timeline.Void(flyInstance);
            flyInstance = null;
        }

        // 只清飞行攻击自己登记的"当前招式"。
        // 飞行中松手放蓄力时，蓄力招已经先登记好了才切状态（FlyCombatState.Exit 会调到这里），
        // 这时再清连段会把刚登记的蓄力招抹掉。
        if (currentNodeIsFly) ResetCombo();
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
                PlayerLog.Info(IsHandAttacking(bufferedCmd)
                    ? $"[连招] {bufferedCmd} 出招中，已存入缓存"
                    : $"[连招] {bufferedCmd} 后摇中（剩 {GetHandRecoveryRemaining(bufferedCmd):F2}s），已存入缓存");
            }
            return false;
        }

        // 重新起手时 ResetCombo 会清掉缓存，先把指令存一份
        InputCmd savedCmd = bufferedCmd;

        // ==================================================
        // 取消权限：当前招式允许被这个键【派生】吗？
        // ==================================================
        ComboNode animating = (sm.currentState == sm.comboState && CurrentInstance != null)
            ? CurrentInstance.node
            : null;

        if (animating != null && !animating.CanBeCanceledBy(bufferedCmd))
        {
            if (verboseLog)
                PlayerLog.Info($"[连招] {animating.nodeName} 演出中不允许被 {bufferedCmd} 打断");
            return false;
        }

        CollectCandidates(bufferedCmd);

        // 普通推进只找非蓄力招 —— 蓄力招走 TryReleaseCharge
        ComboNode match = FindBestMatch(candidateBuffer, bufferedCmd, requiredChargeLevel: 0);

        // ==================================================
        // 【修复】连段打满后自动重新起手
        // ==================================================
        if (match == null && currentNode != null)
        {
            // 【诊断】走到这里说明"当前深度的下一段"一个都没匹配上。
            // 打满三段后走这条是正常的；但如果 A1 之后就走这条，
            // 说明 A2 没配对 —— 下面这条日志能直接看出是哪一环出了问题。
            if (verboseLog)
            {
                PlayerLog.Info(
                    $"[连招诊断] {bufferedCmd} 在深度 {NextDepth(comboDepth)} 找不到后续。" +
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
                PlayerLog.Info($"[连招] 上一套已打满，{bufferedCmd} 作为新连招重新起手");
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

            PlayerLog.Info(
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
                PlayerLog.Info(
                    $"[连招] 蓄满 AA{level} 但找不到对应招式。候选数={candidateBuffer.Count}。\n" +
                    "请检查：① 该武器的 Openers/Follow Ups 里有没有这一级的蓄力招；" +
                    "② 那个节点的 Charge Level 是否等于 " + level + "；" +
                    "③ Is Charge Skill 有没有勾上；" +
                    "④ Input Sequence 末位是不是对应的攻击键。");
            }
            return false;
        }

        // 只清这只手的预输入：另一只手存着的那一下不能被这次放蓄力吞掉
        if (hasBufferedInput && bufferedCmd == cmd) hasBufferedInput = false;
        SetCurrentNode(match, cmd);
        sm.ChangeState(sm.comboState);

        if (verboseLog) PlayerLog.Info($"[连招] 蓄力释放：{match.nodeName} (等级 {match.chargeLevel}/{level})");
        return true;
    }

    // ==================================================
    // 候选解析
    // ==================================================

    /// <summary>
    /// 现场解析候选招式。
    /// </summary>
    /// <param name="cmd">
    /// </param>
    /// <param name="alwaysIncludeOpeners">
    /// </param>
    private void CollectCandidates(InputCmd cmd, bool alwaysIncludeOpeners = false)
    {
        candidateBuffer.Clear();

        WeaponMoveSet weapon = weapons != null ? weapons.GetWeaponForCommand(cmd) : null;

        // ==================================================
        // 【判"有没有连段"要看深度，不能只看指针】
        // ==================================================
        int nextDepth = NextDepth(comboDepth);

        if (currentNode == null || comboDepth <= 0 || nextDepth == 1)
        {
            int depth = 1;

            if (weapon != null)
            {
                AddCandidates(weapon.openers, depth);

                // 蓄力：两个列表一起找
                if (alwaysIncludeOpeners) AddCandidates(weapon.followUps, depth);
            }

            // 【回退条件与下方接续分支对齐】必须加上 weapon == null。
            if (candidateBuffer.Count == 0 && weapon == null)
                AddCandidates(rootNodes, depth);   // 回退：没装武器就用旧配置

            return;
        }

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

    /// <summary>
    /// 登记一次出招：改连段、锁手、算连段窗口，并开一份攻击实例（判定/子弹/特效由它按时间表执行）。
    /// </summary>
    /// <param name="dual">是否双蓄力强化招</param>
    private void SetCurrentNode(ComboNode node, InputCmd triggerCmd,
                                bool dual = false, WeaponMoveSet weaponOverride = null)
    {
        // ---- 回退快照：冲刺/滑铲打断前摇时"本招不算"，段数退回到这里 ----
        ComboNode prevNode = currentNode;
        int prevDepth = comboDepth;
        InputCmd prevCmd = LastTriggerCmd;
        WeaponMoveSet prevWeapon = ActiveWeapon;
        float prevExpire = comboExpireTime;

        // ---- 上一招的动画被这一招顶掉：逻辑照常跑完，只是不再是动画的主人 ----
        AttackInstance prevInst = CurrentInstance;
        if (prevInst != null)
        {
            // 换手抢拍：后摇可以被另一只手顶掉 —— 被顶掉的那只手等到它自己的【攻击段】结束就能再用，
            // 不用把后摇走完（Recovery Time 取消后，"换手可以抢拍"靠这一条保留）
            if (prevInst.ownsAnimation && prevInst.node != null
                && sm.currentState == sm.comboState && prevInst.cmd != triggerCmd)
            {
                int pi = HandIndex(prevInst.cmd);
                if (handAttackId[pi] == prevInst.attackId)
                {
                    float activeEndAt = Mathf.Max(Time.time, prevInst.startTime + prevInst.activeEnd);
                    handAnimEnd[pi] = Mathf.Min(handAnimEnd[pi], activeEndAt);
                    handBusyUntil[pi] = Mathf.Min(handBusyUntil[pi], activeEndAt);
                }
            }

            prevInst.ownsAnimation = false;
        }

        currentNode = node;
        currentNodeIsFly = false;

        // 飞行中放出来的招（或顶掉了一招"要回飞行"的招）→ 进入后摇后自动回到飞行状态
        bool resumeFlight = autoResumeFlightAfterCharge
            && (sm.IsFlying
                || (prevInst != null && prevInst.resumeFlight && sm.currentState == sm.comboState));

        // ---- 段数 ----
        //   双蓄力强化招 → 归零（R7）
        //   单蓄力       → 段数【不变】：下一发平a跟着上一发平a的深度往下接，不看蓄力等级。
        if (dual) comboDepth = 0;
        else if (node.isChargeSkill)
            comboDepth = Mathf.Clamp(comboDepth, 0, MaxComboDepth);
        else comboDepth = NextDepth(comboDepth);

        LastTriggerCmd = triggerCmd;

        // ---- 出招流水号：给「刚放完蓄力」的按手判断用 ----
        if (dual || node.isChargeSkill)
        {
            int serial = ++actionSerial;
            lastChargeReleaseSerial[HandIndex(triggerCmd)] = serial;
            if (dual) lastChargeReleaseSerial[HandIndex(OtherHand(triggerCmd))] = serial;   // 强化招两只手都算放过
        }
        else
        {
            lastNormalAttackSerial = ++actionSerial;
        }

        ActiveWeapon = weaponOverride != null
            ? weaponOverride
            : (weapons != null ? weapons.GetWeaponForCommand(triggerCmd) : null);

        // ---- 锁手：到这一招的总时长（前摇 + 攻击 + 后摇）走完。另一只手不受影响（强化招两只手一起锁）----
        float duration = AttackTimelineRunner.ExpectedDuration(node);
        const float recovery = 0f;   // 旧的"动画播完再加一段 Recovery Time"已并入总时长

        int id = MarkHandBusy(triggerCmd, duration, recovery);
        if (dual) MarkHandBusy(OtherHand(triggerCmd), duration, recovery);

        // ---- 连段窗口：总时长走完 + comboWindow ----
        comboExpireTime = Time.time + duration + recovery + node.comboWindow + ComboWindowTolerance;

        // ---- 攻击实例 ----
        AttackInstance inst = timeline.Begin(node, ActiveWeapon, triggerCmd);
        inst.attackId = id;
        inst.isChargeRelease = node.isChargeSkill;
        inst.isDual = dual;
        inst.resumeFlight = resumeFlight;
        inst.prevNode = prevNode;
        inst.prevDepth = prevDepth;
        inst.prevCmd = prevCmd;
        inst.prevWeapon = prevWeapon;
        inst.prevExpire = prevExpire;
        CurrentInstance = inst;

        if (verboseLog)
        {
            string w = ActiveWeapon != null ? ActiveWeapon.displayName : "无武器";
            PlayerLog.Info($"[连招] 第{comboDepth}段：{node.nodeName}（{w}）");
        }
    }

    /// <summary>
    /// 攻击动画播完（动画事件 OnAttackAnimationEnd / ComboState 的兜底超时 / 总时长走完）。
    /// </summary>
    public void StartGracePeriod()
    {

        if (CurrentInstance != null) CurrentInstance.ownsAnimation = false;
    }

    // ==================================================
    // 【实验 · R1】移动 / 冲刺 / 滑铲对攻击的打断
    // ==================================================

    /// <summary>
    /// 【R1 · 前摇被移动取消】方向键 / 跳跃 / 飞行在前摇里取消了当前这一招：
    /// 本招作废（之后不会出刀出弹），连段归零。这只手立刻可用。
    /// </summary>
    public void VoidCurrentAttackAndResetCombo()
    {
        AttackInstance inst = CurrentInstance;
        if (inst != null)
        {
            timeline.Void(inst);
            inst.ownsAnimation = false;

            FreeHand(inst.cmd);
            if (inst.isDual) FreeHand(OtherHand(inst.cmd));

            if (verboseLog && inst.node != null)
                PlayerLog.Info($"[连招] {inst.node.nodeName} 前摇被移动取消 → 本招作废，连段归零");
        }

        lastSingle.valid = false;
        ResetCombo();
    }

    /// <summary>
    /// 【R1 · 冲刺 / 滑铲】任何阶段可用，永不打断连段，换手不用等。
    ///   打断前摇       → 本招不算：段数退回出招之前（A1 → A2 前摇被冲刺打断 → 下一下仍是第 2 段）
    ///   打断判定 / 后摇 → 本招算结束，不用等后摇（同手也不用）；剩余判定立刻掐断
    /// 不在攻击动画里时冲刺 / 滑铲同样免掉剩余后摇。
    /// </summary>
    /// <param name="wasInCombo">冲出去之前是否在攻击动画里</param>
    /// <param name="phase">冲出去之前当前这一招所处的阶段（CurrentAttackPhase）</param>
    public void OnDashSlideInterrupt(bool wasInCombo, AttackPhase phase)
    {
        AttackInstance inst = CurrentInstance;

        if (wasInCombo && phase == AttackPhase.Windup && inst != null && inst.alive && !inst.isFly)
        {
            currentNode = inst.prevNode;
            comboDepth = currentNode != null ? inst.prevDepth : 0;
            LastTriggerCmd = inst.prevCmd;
            ActiveWeapon = inst.prevWeapon;

            // 退回后至少留一个上一招的 comboWindow，免得刚退回就超时
            if (currentNode != null)
                comboExpireTime = Mathf.Max(inst.prevExpire, Time.time + currentNode.comboWindow + ComboWindowTolerance);

            if (verboseLog && inst.node != null)
                PlayerLog.Info($"[连招] {inst.node.nodeName} 前摇被冲刺/滑铲打断 → 本招不算，段数退回 {comboDepth}");
        }
        else if (verboseLog && wasInCombo && inst != null && inst.node != null)
        {
            PlayerLog.Info($"[连招] {inst.node.nodeName} 被冲刺/滑铲打断（{phase}）→ 本招算结束，免后摇，段数保持 {comboDepth}");
        }

        if (inst != null) inst.ownsAnimation = false;

        // 剩余判定 / 连射立刻掐断（包括动画已被顶掉、逻辑还在跑的旧招）
        timeline.VoidAll(includeFly: false);

        // 不用等后摇：两只手都立刻可用
        FreeHand(InputCmd.MainAttack);
        FreeHand(InputCmd.SubAttack);

        pendingDual.active = false;
        lastSingle.valid = false;
    }

    /// <summary>
    /// 清空连段。
    ///
    /// 【注意】不清手部后摇，也不动正在跑的攻击实例 —— 连段断了不代表手就闲下来了。
    /// </summary>
    public void ResetCombo()
    {
        currentNode = null;
        currentNodeIsFly = false;
        comboDepth = 0;
        hasBufferedInput = false;
        ActiveWeapon = null;
    }

    /// <summary>当前招式能否被位移动作打断。供 PlayerCommandRouter 查询</summary>
    public bool CanCurrentNodeBeCanceledByMovement()
        => currentNode == null || currentNode.CanBeCanceledByMovement;
}
