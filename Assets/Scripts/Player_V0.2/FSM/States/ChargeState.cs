using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【蓄力控制器】
///
/// 按住 → 逐级爬升 0→1→2→3 → 松手才放，3 级封顶但可以无限期举着。
/// 起始等级由上一招的 chargeStartLevelAfter 决定（连段是蓄力的助跑）。
///
/// ==========================================================
/// 【批次O 新增 1】携带动量
/// 滑铲途中蓄力时滑行继续，滑到终点后玩家可以继续举着或打出。
/// 减速逻辑在 SlideMomentum 里，本状态只是接手。
///
/// 【批次O 新增 2】蓄力突刺（AAn + 单按 shift）
///
/// 玩家用【一次 dash 的 CD】换【一级蓄力】——
/// 蓄到 1 级按 shift 打出 2 级，蓄到 2 级打出 3 级。
/// 这是明确的资源置换：想跳级就得压上冲刺，冲刺 CD 期间失去位移手段。
///
/// 三种情况（与设计文档一致）：
///   正常          → 冲到最远端，打出 AA(n+1)
///   途中撞到敌人  → 立刻对第一个敌人打出
///   装了 Relay    → 无视途中敌人，一定冲到最远端
///
/// 最后一条没有写死"检查是不是 Relay"，而是问宝石
/// IgnoresThrustInterruption —— 以后想让别的宝石也有这个特性，
/// 改那颗宝石自己就行，本文件不用动。
/// ==========================================================
/// </summary>
public class ChargeState : PlayerStateBase
{
    private const float ChargeMoveSpeedMultiplier = 0.2f;

    /// <summary>突刺途中检测敌人的半径</summary>
    private const float ThrustHitRadius = 1.0f;

    // ---- 对外广播 ----
    /// <summary>蓄力等级变化时广播 (新等级 0~3)。UI 与特效订阅这个</summary>
    public event System.Action<int> OnChargeLevelChanged;

    /// <summary>蓄力进度变化时广播 (0~1，用于蓄力条填充)</summary>
    public event System.Action<float> OnChargeProgressChanged;

    /// <summary>蓄力突刺开始时广播。拖尾、音效订阅这个</summary>
    public event System.Action OnThrustStarted;

    // ---- 蓄力运行时 ----
    private InputCmd chargingCmd;
    private WeaponSlot chargingSlot;
    private WeaponMoveSet chargingWeapon;

    /// <summary>起始等级折算出的时间下限。实际蓄力时间 = max(按住时长, 本值)</summary>
    private float floorTime;

    /// <summary>本帧的有效蓄力时间。每帧重算，不再自行累加</summary>
    private float elapsed;

    /// <summary>进入蓄力架势后经过的时间。助跑偏移要靠它才不会被追平</summary>
    private float timeInStance;

    private int currentLevel;
    private float originalGravity;
    private bool isValidCharge;

    // ---- 突刺运行时 ----
    private bool isThrusting;
    private float thrustTimer;
    private float thrustDirection;
    private bool thrustIgnoresEnemies;

    private ComboInputBuffer buffer;
    private WeaponLoadout weapons;
    private TargetFinder targetFinder;

    public int CurrentChargeLevel => currentLevel;
    public bool IsThrusting => isThrusting;

    public ChargeState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private WeaponLoadout Weapons
        => weapons != null ? weapons : (weapons = sm.GetComponent<WeaponLoadout>());

    private TargetFinder Finder
        => targetFinder != null ? targetFinder : (targetFinder = sm.GetComponent<TargetFinder>());

    // ==========================================================
    // 生命周期
    // ==========================================================

    public override void Enter()
    {
        originalGravity = sm.rb.gravityScale;

        isThrusting = false;
        thrustTimer = 0f;

        // 快照机制：进入的瞬间拍下在蓄哪个键，
        // 之后只问 Controller 的物理按压状态，不再依赖 currentNode ——
        // 因为连招系统可能在蓄力途中把它清空。
        chargingCmd = Buffer.LastTriggerCmd;
        WeaponMoveSet.TryCommandToSlot(chargingCmd, out chargingSlot);
        chargingWeapon = Weapons != null ? Weapons.GetWeapon(chargingSlot) : null;

        int startLevel = Buffer.PendingChargeStartLevel;

        // 闸门：武器缺失 或 蓄力 CD 中
        isValidCharge = (chargingWeapon != null)
                        && (Weapons == null || Weapons.IsChargeReady(chargingSlot));

        if (!isValidCharge)
        {
            // 仍然进架势（有蓄力动作表现），但松手不会产出招式。
            // 直接踢回 Idle 会让「CD 中按住攻击」变成毫无反馈，手感更差。
            floorTime = 0f;
            timeInStance = 0f;
            elapsed = 0f;
            currentLevel = 0;
        }
        else
        {
            // 【计时口径】蓄力时间从【按下攻击键那一刻】起算，
            // 而不是从进入蓄力架势起算 —— 攻击动画那段时间也计入。
            // 起始等级因此从「起点」变成「下限」。
            floorTime = chargingWeapon.GetTimeForLevel(startLevel);
            timeInStance = 0f;
            elapsed = CalcElapsed();
            currentLevel = chargingWeapon.GetLevelForTime(elapsed);
        }

        sm.anim.Play(PlayerAnimHash.Charge);
        OnChargeLevelChanged?.Invoke(currentLevel);
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
            ReleaseCharge(currentLevel);
            return;
        }

        if (isValidCharge)
        {
            timeInStance += Time.deltaTime;
            elapsed = CalcElapsed();

            int newLevel = chargingWeapon.GetLevelForTime(elapsed);

            // 3 级封顶后可以无限期举着，等级不再变化
            if (newLevel != currentLevel)
            {
                currentLevel = newLevel;
                OnChargeLevelChanged?.Invoke(currentLevel);
            }

            OnChargeProgressChanged?.Invoke(GetProgressToNextLevel());
        }

        if (sm.IsGrounded())
        {
            UpdateFacing(sm.playerController.moveInput.x);
        }
    }

    public override void FixedUpdate()
    {
        // ---- 突刺位移优先级最高 ----
        if (isThrusting)
        {
            sm.rb.linearVelocity = new Vector2(thrustDirection * sm.dashSpeed, 0f);
            return;
        }

        // ---- 携带动量：滑铲途中蓄力时滑行继续 ----
        if (sm.slideMomentum.Tick(sm, Time.fixedDeltaTime)) return;

        // ---- 常规蓄力站位 ----
        if (sm.IsGrounded())
        {
            sm.rb.gravityScale = originalGravity;
            ApplyHorizontalMove(ChargeMoveSpeedMultiplier);
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
        // 退出蓄力有好几条路径（打出、等级不够、受击打断、冲刺打断、死亡），
        // 只要漏掉任何一条，蓄力光效就会留在角色身上不消失 ——
        // 而那是个池化对象，不回收就是池泄漏。Exit 是所有路径的必经之地。
        if (currentLevel != 0)
        {
            currentLevel = 0;
            OnChargeLevelChanged?.Invoke(0);
        }
    }

    // ==========================================================
    // 蓄力突刺
    // ==========================================================

    /// <summary>
    /// 由 PlayerCommandRouter 调用：蓄力中【单按】冲刺键。
    /// </summary>
    /// <returns>是否成功起势</returns>
    public bool TryStartThrust()
    {
        if (isThrusting) return false;
        if (!isValidCharge || currentLevel <= 0) return false;

        // 资源置换的核心：真的消耗一次冲刺
        if (!sm.dashSkill.CanExecute()) return false;
        sm.dashSkill.Execute();

        isThrusting = true;
        thrustTimer = sm.dashDuration;
        thrustDirection = sm.playerController.facingDirection;

        // 携带动量在突刺期间让位
        sm.slideMomentum.Clear();

        sm.rb.gravityScale = 0f;
        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        // 问宝石：要不要无视途中的敌人？
        // 没有写死"检查是不是 Relay" —— 以后想让别的宝石也有这个特性，
        // 改那颗宝石自己就行，本文件不用动。
        thrustIgnoresEnemies = sm.loadout != null && sm.loadout.IgnoresThrustInterruption();

        OnThrustStarted?.Invoke();
        return true;
    }

    private void TickThrust()
    {
        thrustTimer -= Time.deltaTime;

        // ---- 途中撞到敌人 → 立刻打出 ----
        if (!thrustIgnoresEnemies && Finder != null)
        {
            Transform target = Finder.FindNearest(sm.transform.position, ThrustHitRadius);
            if (target != null)
            {
                // 对着目标出招，避免背对着放
                int facing = Finder.GetFacingTowards(target);
                if (facing != 0) sm.playerController.SetFacingDirection(facing);

                FinishThrust();
                return;
            }
        }

        // ---- 冲到最远端 ----
        if (thrustTimer <= 0f) FinishThrust();
    }

    private void FinishThrust()
    {
        isThrusting = false;
        sm.rb.gravityScale = originalGravity;
        sm.rb.linearVelocity = Vector2.zero;
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;

        // 跳一级。3 级仍为 3 级（封顶）
        int fireLevel = Mathf.Min(currentLevel + 1, WeaponMoveSet.MaxChargeLevel);
        ReleaseCharge(fireLevel);
    }

    // ==========================================================
    // 释放
    // ==========================================================

    private void ReleaseCharge(int level)
    {
        if (!isValidCharge || level <= 0)
        {
            FallbackToNeutral();
            return;
        }

        bool fired = Buffer.TryReleaseCharge(chargingCmd, level);

        if (fired)
        {
            Weapons?.StartChargeCooldown(chargingSlot);
            return;   // ChangeState 已由 Buffer 内部发起
        }

        // 蓄满了但没有匹配的招式（招式表没配全）→ 正常收招
        FallbackToNeutral();
    }

    private void FallbackToNeutral()
    {
        // 归零广播交给 Exit() 统一处理，这里不重复发
        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else HandleLanding();
    }

    /// <summary>
    /// 本帧的有效蓄力时间。
    ///
    /// PlayerController 从按下攻击键那一刻就在累计 holdTime，
    /// 攻击动画播放期间也照常走字 —— 所以直接读它即可，
    /// Inspector 上的阈值就等于玩家真实的按住时长，所见即所得。
    ///
    /// 再和 floorTime 取大者，保证「连段越长起步越高」不被短按绕过。
    /// </summary>
    private float CalcElapsed()
    {
        float hold = (chargingCmd == InputCmd.MainAttack)
            ? sm.playerController.mainAttackHoldTime
            : sm.playerController.subAttackHoldTime;

        // 【助跑必须是偏移量，不能只是取大者】
        //
        // 曾经写的是 Mathf.Max(hold, floorTime)，结果是：
        // 打完 A3 进架势时 floorTime=0.9 让等级立刻跳到 2 级，
        // 但 hold 自己的钟还在从 0.5 慢慢爬 —— 等它爬过 0.9 之后
        // elapsed 就跟着 hold 走了，那 0.4 秒的助跑被追平，等于白给。
        // 表现出来就是「连招后 AA2→AA3 比从头蓄还慢」。
        //
        // 比喻：赛跑给你让了 40 米，但发令枪一响你还是从起跑线开始跑。
        //
        // 正确做法是让助跑成为持续有效的偏移：
        // 进架势那一刻正好等于 floorTime，之后每一秒都实打实往前推。
        return Mathf.Max(hold, floorTime + timeInStance);
    }

    /// <summary>当前等级到下一级的进度 0~1。已满级时恒为 1</summary>
    private float GetProgressToNextLevel()
    {
        if (chargingWeapon == null) return 0f;
        if (currentLevel >= WeaponMoveSet.MaxChargeLevel) return 1f;

        float from = chargingWeapon.GetTimeForLevel(currentLevel);
        float to = chargingWeapon.GetTimeForLevel(currentLevel + 1);

        if (to <= from) return 1f;
        return Mathf.Clamp01((elapsed - from) / (to - from));
    }
}
