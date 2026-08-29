using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【蓄力控制器】—— P2 重写。
///
/// ==========================================================
/// 【核心模型变了：从"爬楼梯"变成"一次一跳"】
///
/// 旧模型：按住不放 0→1→2→3 一路爬，想在哪层松手就在哪层。
/// 新模型：一次蓄力只升【一级】，蓄满即封顶，继续按住也不会再涨。
///
///     蓄力目标等级 = 上次普攻的段数（封顶 3）
///     蓄力起始等级 = 目标 - 1
///
/// 于是蓄力等级不再由"按多久"决定，而是由【连段深度】决定 ——
/// 想打出 AA3，必须先打满三段普攻。
///
/// 【三种结束方式，后果完全不同】
///   ① 蓄满后松手 → 打出招式，同手进后摇，连段重新计数
///   ② 没蓄满松手 → 什么都不放，【连段清零】
///   ③ 被打断     → 同 ②
///
/// 蓄力因此是一场赌注：压上当前连段，赌自己能撑到蓄满。
/// 撑不住就血本无归 —— 这是蓄力招威力的代价，
/// 也正是 ctrl 能腾出来做碰撞箱调整的原因（取消连段的职责已经由蓄力失败承担）。
/// ==========================================================
///
/// 【尚未实现 · 等 P3】
/// 说明书 3.6 写了「蓄力期间可以跳跃」，但跳跃 = 切进 JumpState
/// = ChargeState.Exit = 蓄力直接没了。
/// 要支持它必须先把蓄力从"状态"解耦成"随身模块"，那是 P3 的事。
/// 在那之前 PlayerCommandRouter 会明确拦下蓄力中的跳跃并打日志 ——
/// 让它悄悄清掉玩家蓄了半天的力是更糟的体验。
/// </summary>
public class ChargeState : PlayerStateBase
{
    // ---- 对外广播 ----
    /// <summary>蓄力等级变化时广播 (0 = 未蓄满, target = 已蓄满)。UI 与特效订阅这个</summary>
    public event System.Action<int> OnChargeLevelChanged;

    /// <summary>蓄力进度 0~1。蓄满后恒为 1</summary>
    public event System.Action<float> OnChargeProgressChanged;

    /// <summary>蓄力突刺开始时广播</summary>
    public event System.Action OnThrustStarted;

    // ---- 蓄力运行时 ----
    private InputCmd chargingCmd;
    private WeaponSlot chargingSlot;
    private WeaponMoveSet chargingWeapon;

    /// <summary>本次蓄力的目标等级。进入时锁定，全程不变</summary>
    private int targetLevel;

    /// <summary>蓄满这一级需要多少秒（已套用加速倍率）</summary>
    private float requiredTime;

    /// <summary>已经蓄了多久</summary>
    private float chargedTime;

    /// <summary>是否已经蓄满。蓄满后可以无限期举着</summary>
    private bool isCharged;

    private bool isValidCharge;
    private float originalGravity;

    // ---- 突刺运行时 ----
    private bool isThrusting;
    private float thrustTimer;
    private float thrustDirection;
    private bool thrustIgnoresEnemies;

    private ComboInputBuffer buffer;
    private WeaponLoadout weapons;
    private TargetFinder targetFinder;

    /// <summary>当前蓄力等级：没蓄满是 0，蓄满是目标等级。供 UI/特效用</summary>
    public int CurrentChargeLevel => isCharged ? targetLevel : 0;

    /// <summary>本次蓄力的目标等级（无论蓄没蓄满）</summary>
    public int TargetChargeLevel => targetLevel;

    public bool IsCharged => isCharged;
    public bool IsThrusting => isThrusting;

    public ChargeState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private WeaponLoadout Weapons
        => weapons != null ? weapons : (weapons = sm.GetComponent<WeaponLoadout>());

    private TargetFinder Finder
        => targetFinder != null ? targetFinder : (targetFinder = sm.GetComponent<TargetFinder>());

    private float MoveMultiplier
    {
        get
        {
            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            return cfg != null ? cfg.chargeMoveSpeedMultiplier : 0.2f;
        }
    }

    // ==========================================================
    // 生命周期
    // ==========================================================

    public override void Enter()
    {
        originalGravity = sm.rb.gravityScale;

        isThrusting = false;
        thrustTimer = 0f;
        chargedTime = 0f;
        isCharged = false;

        // 快照：进入的瞬间拍下在蓄哪只手。
        // 之后只问 Controller 的物理按压状态，不再依赖 currentNode ——
        // 因为连招系统可能在蓄力途中把它清空。
        chargingCmd = Buffer.LastTriggerCmd;
        WeaponMoveSet.TryCommandToSlot(chargingCmd, out chargingSlot);
        chargingWeapon = Weapons != null ? Weapons.GetWeapon(chargingSlot) : null;

        // 目标等级由连段深度锁定，全程不变
        targetLevel = Buffer.ChargeTargetLevel;

        isValidCharge = (chargingWeapon != null)
                        && (Weapons == null || Weapons.IsChargeReady(chargingSlot));

        requiredTime = isValidCharge ? CalcRequiredTime() : float.MaxValue;

        if (!isValidCharge && Buffer.verboseLog)
        {
            Debug.Log(chargingWeapon == null
                ? "[蓄力] 该手没有武器，无法蓄力"
                : "[蓄力] 该手蓄力 CD 中，本次蓄不出东西");
        }
        else if (Buffer.verboseLog)
        {
            Debug.Log($"[蓄力] 目标 AA{targetLevel}，需要 {requiredTime:F2}s");
        }

        sm.anim.Play(PlayerAnimHash.Charge);
        OnChargeLevelChanged?.Invoke(0);
        OnChargeProgressChanged?.Invoke(0f);
    }

    /// <summary>
    /// 蓄满这一级需要多久。
    ///
    /// 基准 = 目标等级的累计时间 - 起始等级的累计时间，
    /// 也就是武器上那三个阈值之间的差值。
    ///
    /// 接在普攻后面的蓄力享受加速倍率（说明书：「普攻后的同手蓄力速度加快」），
    /// 原地起手则没有这个优待。
    /// </summary>
    private float CalcRequiredTime()
    {
        int start = Mathf.Max(0, targetLevel - 1);

        float span = chargingWeapon.GetTimeForLevel(targetLevel)
                     - chargingWeapon.GetTimeForLevel(start);

        span = Mathf.Max(0.01f, span);

        if (Buffer.IsChargeAfterCombo)
        {
            float mul = Mathf.Max(0.01f, chargingWeapon.comboChargeSpeedMultiplier);
            span /= mul;
        }

        return span;
    }

    public override void Update()
    {
        // ---- 突刺进行中：不接受松手释放，先冲完 ----
        if (isThrusting)
        {
            TickThrust();
            return;
        }

        bool isHeld = (chargingCmd == InputCmd.MainAttack)
            ? sm.playerController.isMainAttackHeld
            : sm.playerController.isSubAttackHeld;

        if (!isHeld)
        {
            HandleRelease();
            return;
        }

        // ---- 蓄力推进 ----
        if (isValidCharge && !isCharged)
        {
            chargedTime += Time.deltaTime;

            OnChargeProgressChanged?.Invoke(Mathf.Clamp01(chargedTime / requiredTime));

            if (chargedTime >= requiredTime)
            {
                isCharged = true;
                OnChargeProgressChanged?.Invoke(1f);
                OnChargeLevelChanged?.Invoke(targetLevel);

                if (Buffer.verboseLog) Debug.Log($"[蓄力] 蓄满 AA{targetLevel}，松手即发");
            }
        }

        // 蓄满后可以无限期举着，什么都不做

        if (sm.IsGrounded()) UpdateFacing(sm.playerController.moveInput.x);
    }

    public override void FixedUpdate()
    {
        if (isThrusting)
        {
            sm.rb.linearVelocity = new Vector2(thrustDirection * sm.dashSpeed, 0f);
            return;
        }

        // 携带动量：滑铲途中蓄力时滑行继续
        if (sm.slideMomentum.Tick(sm, Time.fixedDeltaTime)) return;

        if (sm.IsGrounded())
        {
            sm.rb.gravityScale = originalGravity;
            ApplyHorizontalMove(MoveMultiplier);
        }
        else
        {
            // 空中蓄力：反重力钉在原地
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }
    }

    public override void Exit()
    {
        sm.rb.gravityScale = originalGravity;
        isThrusting = false;

        // 统一在这里广播归零。
        // 退出蓄力有好几条路径（打出、未蓄满松手、受击打断、死亡），
        // 漏掉任何一条，蓄力光效就会留在角色身上不消失 ——
        // 而那是个池化对象，不回收就是池泄漏。Exit 是所有路径的必经之地。
        OnChargeLevelChanged?.Invoke(0);
        OnChargeProgressChanged?.Invoke(0f);
    }

    // ==========================================================
    // 释放
    // ==========================================================

    /// <summary>
    /// 松手。蓄满与否决定了完全不同的两种后果。
    /// </summary>
    private void HandleRelease()
    {
        // ---- 没蓄满 → 什么都不放，连段清零 ----
        if (!isCharged)
        {
            if (Buffer.verboseLog)
                Debug.Log($"[蓄力] 未蓄满就松手（{chargedTime:F2}/{requiredTime:F2}s），连段清零");

            // 【不会退而求其次打出低一级的招】——
            // 这正是"蓄力是一场赌注"的实现处
            Buffer.ResetCombo();
            FallbackToNeutral();
            return;
        }

        // ---- 蓄满 → 打出目标等级 ----
        FireCharge(targetLevel);
    }

    private void FireCharge(int level)
    {
        bool fired = Buffer.TryReleaseCharge(chargingCmd, level);

        if (fired)
        {
            Weapons?.StartChargeCooldown(chargingSlot);
            // ChangeState 已由 Buffer 内部发起
            return;
        }

        // 蓄满了但招式表没配全 → 正常收招
        if (Buffer.verboseLog)
            Debug.LogWarning($"[蓄力] 蓄满 AA{level} 但没有匹配的招式，请检查武器 Follow Ups");

        Buffer.ResetCombo();
        FallbackToNeutral();
    }

    private void FallbackToNeutral()
    {
        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else HandleLanding();
    }

    // ==========================================================
    // 蓄力突刺（蓄力中单按冲刺键）
    // ==========================================================

    /// <summary>
    /// 【P2 调整】突刺不再「跳级」——
    /// 蓄力只能升一级之后，跳级设计已经冗余（说明书已删去该规则）。
    ///
    /// 现在蓄力中的 Shift/Ctrl 是【纯位移】，由 PlayerCommandRouter 直接走
    /// 普通冲刺/滑铲，不再经过本方法。保留它是为了不破坏已有引用。
    /// </summary>
    [System.Obsolete("蓄力中的位移已改为普通冲刺，不再跳级")]
    public bool TryStartThrust()
    {
        return false;
    }

    private void TickThrust()
    {
        thrustTimer -= Time.deltaTime;
        if (thrustTimer <= 0f) FinishThrust();
    }

    private void FinishThrust()
    {
        isThrusting = false;
        sm.rb.gravityScale = originalGravity;
        sm.rb.linearVelocity = Vector2.zero;
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
    }
}