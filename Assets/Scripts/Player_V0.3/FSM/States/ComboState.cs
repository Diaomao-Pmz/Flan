using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 连招执行状态。
/// </summary>
public class ComboState : PlayerStateBase
{
    /// <summary>超时倍率：实际上限 = 动画长度 × 本值</summary>
    private const float TimeoutMultiplier = 1.5f;

    /// <summary>动画剪辑缺失时的兜底上限（秒）</summary>
    private const float FallbackTimeout = 2.0f;

    public bool isCancelable = false;

    private float originalGravity;
    private bool wasAirborneOnEnter;

    // ---- 出招位移：判定段开始时启动，匀速走完一段精确距离；被打断就停在半路 ----
    private Vector2 thrustDistance;     // 这次要走的总位移（已乘朝向）
    private float thrustDuration;       // 用多久走完
    private float thrustTimer;          // 已经走了多久
    private bool thrustPending;         // 准备好了，等起步的那个阶段开始
    private float thrustStartAt;        // 出招后第几秒起步（所选阶段的开头）
    private bool thrustRunning;         // 正在走
    private bool thrustEndPending;      // 刚走完，下一物理帧把位移给的速度收掉
    private GuardedMove thrustGuard;    // 终点裁判：位移不停在 Boss 身体里（见 DashEndpointGuard）
    private bool clashThrustActive;     // 正在走的是拼刀成功后的位移（顶掉了本招原来的位移）
    private bool clashThrustPassThrough;

    // 碰撞箱微调是否生效中
    private bool isBodyAdjusting;

    /// <summary>
    /// 本招是哪只手打出的。
    /// </summary>
    private InputCmd ownerCmd;

    /// <summary>本次出招的编号。Exit 时用它核对"我还是不是最新的那一次"</summary>
    private int ownerAttackId;

    /// <summary>
    /// 本招用的武器。在 Enter 时拍快照 ——
    /// 招式演到一半被位移打断时 Buffer 会 ResetCombo，ActiveWeapon 会被清成 null。
    /// </summary>
    private WeaponMoveSet ownerWeapon;

    /// <summary>
    /// 【实验】本招的攻击实例（判定/子弹/特效与阶段都在它身上）。
    /// 本状态只管"动画 + 身体"：动画被顶掉或取消后，实例照常自己跑完。
    /// </summary>
    private AttackInstance instance;

    /// <summary>出招那一刻方向键的左右（-1/0/1）。前摇里只有【出招之后新按下/换向】才算移动取消</summary>
    private int baselineMoveSign;

    // 兜底超时
    private float timeoutLimit;
    private float elapsed;
    private bool hasWarnedTimeout;
    private ComboNode activeNode;

    // 缓存引用，避免每帧 GetComponent
    private ComboInputBuffer buffer;
    private PlayerHitDetection hitDetection;
    private PlayerWeaponEmitter weaponEmitter;
    private PlayerAimProvider aimProvider;

    public ComboState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        thrustGuard = new GuardedMove(stateMachine);
    }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    private PlayerWeaponEmitter WeaponEmitter
        => weaponEmitter != null ? weaponEmitter : (weaponEmitter = sm.GetComponent<PlayerWeaponEmitter>());

    private PlayerAimProvider AimProvider
        => aimProvider != null ? aimProvider : (aimProvider = sm.GetComponent<PlayerAimProvider>());


    public override void Enter()
    {
        isCancelable = false;
        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
        wasAirborneOnEnter = !sm.IsGrounded();

        isBodyAdjusting = false;
        instance = Buffer.CurrentInstance;
        activeNode = Buffer.currentNode;
        ownerCmd = Buffer.LastTriggerCmd;
        ownerAttackId = Buffer.GetHandAttackId(ownerCmd);
        ownerWeapon = Buffer.ActiveWeapon;
        baselineMoveSign = MoveSign();

        // 空中连段反重力悬停：把角色钉在空中，防止挥剑时诡异下滑
        if (wasAirborneOnEnter)
        {
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }

        // ---- 兜底超时：按动画长度算上限 ----
        elapsed = 0f;
        hasWarnedTimeout = false;

        float clipLength = (activeNode != null && activeNode.attackClip != null)
            ? activeNode.attackClip.length
            : 0f;

        // 动画会按 ComboNode 的三段时长调速，实际可能比剪辑原长更久，所以取两者中较长的
        float expected = Mathf.Max(clipLength, instance != null ? instance.duration : 0f);
        timeoutLimit = (expected > 0f)
            ? expected * TimeoutMultiplier
            : FallbackTimeout;

        if (activeNode != null)
        {
            if (string.IsNullOrEmpty(activeNode.animName))
            {
                // 这条以前是静默失败 —— anim.Play("") 什么都不做，角色卡在上一个动画上
                Debug.LogWarning(
                    $"[ComboState] 招式「{activeNode.nodeName}」没有指定 Attack Clip，" +
                    "动画不会播放。请检查 ComboNode 资产。", sm);
            }
            else
            {
                sm.animDriver.SetBase(activeNode.animName, restart: true);
                sm.animDriver.SetAttackOwner(instance);   // 按三段时长给攻击动画调速
            }

            SetupThrust();
        }
    }

    public override void Update()
    {
        // ---- 兜底超时检查 ----
        elapsed += Time.deltaTime;
        if (elapsed > timeoutLimit)
        {
            WarnAndExitOnTimeout();
            return;
        }

        // 【实验】飞行中放出来的蓄力招：进入后摇阶段就回到飞行状态
        if (TryResumeFlight(force: false)) return;

        // 【实验】这一招的总时长（前摇 + 攻击 + 后摇）走完了 → 收招。
        // 动画按三段时长调速，正常情况下动画播完（OnAttackAnimationEnd）和总时长走完几乎同时；
        // 哪个先到都走同样的收招流程。
        if (instance != null && instance.elapsed >= instance.duration)
        {
            Buffer.StartGracePeriod();

            if (TryResumeFlight(force: true)) return;

            if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
            else HandleLanding();
            return;
        }

        // 后摇取消检测
        if (isCancelable)
        {
            if (Buffer.TryAdvanceCombo()) return;
        }

        // 【实验 · R1】前摇 / 后摇里的方向键移动取消（跳跃、飞行、冲刺、滑铲由路由器处理）
        if (TryMovementCancel()) return;

        // 【P1b 改动】攻击动画中【不再】响应方向键转向。

        // ---- 碰撞箱微调（不打断攻击）----
        TickBodyAdjust();
    }

    public override void FixedUpdate()
    {
        // ---- 出招位移（判定段开始时起步）----
        bool thrusting = TickThrust();
        UpdateThrustBulletCancel();
        if (thrusting) return;

        // ---- 冲量结束后锁死移动 ----
        if (sm.IsGrounded())
        {
            if (HoldingStill)
            {
                // 特殊招式可能允许在某一段里慢慢走（只改速度，不转身）
                float mult = activeNode.ActiveSpecial.MoveSpeedMultiplier(
                    instance, sm.playerController.moveInput.x, sm.playerController.facingDirection);

                if (mult > 0f) ApplyHorizontalMove(mult);
                else sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
                return;
            }

            if (activeNode == null || activeNode.lockMovementDuringAttack)
            {
                // 【蓄力准备窗口】已经按住攻击键准备蓄力 → 可以拖着步子挪（衰减在 ApplyHorizontalMove 里）。
                // 攻击动画照常播、朝向不变，所以能边收招边后撤。
                if (Buffer != null && Buffer.IsInPreChargeWindow) ApplyHorizontalMove();
                else sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
            }
            return;
        }

        // 空中连招期间锁死速度，不接受重力叠加
        sm.rb.linearVelocity = Vector2.zero;
        // 注意：位移期间 TickThrust 已经 return 了，走不到这里 ——
        // 所以空中的蓄力位移不会被这句抹掉
    }

    /// <summary>
    /// 【实验】飞行中松手放蓄力 → 人先变回地面状态把这一招打出来 → 进入后摇阶段后回到飞行状态。
    /// </summary>
    /// <param name="force">动画已经播完（没有后摇可等了）：不看阶段，直接回</param>
    /// <returns>是否已经切回飞行状态</returns>
    public bool TryResumeFlight(bool force)
    {
        if (instance == null || !instance.resumeFlight) return false;

        if (!force && Buffer.MovementPhase != AttackPhase.Recovery) return false;

        instance.resumeFlight = false;

        // MP 耗尽就不回了，照常往下掉
        if (sm.playerState != null && sm.playerState.health.currentMP <= 0) return false;

        sm.ChangeState(sm.flyState);
        return true;
    }

    private int MoveSign()
    {
        float x = sm.playerController.moveInput.x;
        return Mathf.Abs(x) > 0.1f ? (x > 0f ? 1 : -1) : 0;
    }

    /// <summary>
    /// 【实验 · R1】平a三阶段与方向键移动。
    /// </summary>
    /// <returns>是否已经切走状态</returns>
    private bool TryMovementCancel()
    {
        if (Buffer == null) return false;
        if (HoldingStill) return false;   // 特殊招式：前摇里按方向键也不取消，判定段不能动，到后摇才按平常的规矩
        if (activeNode != null && !activeNode.lockMovementDuringAttack) return false;

        int sign = MoveSign();
        bool fresh = sign != 0 && sign != baselineMoveSign;

        // 松开过 → 之后再按就是新输入
        if (sign == 0) baselineMoveSign = 0;

        if (sign == 0) return false;
        if (IsThrustActive) return false;

        AttackPhase phase = Buffer.MovementPhase;

        if (phase == AttackPhase.Windup)
        {
            if (!fresh) return false;

            Buffer.VoidCurrentAttackAndResetCombo();
            LeaveToMove();
            return true;
        }

        if (phase == AttackPhase.Recovery)
        {
            // 连段保留；这只手的锁（到总时长结束）在出招时就算好了，不用动
            LeaveToMove();
            return true;
        }

        return false;   // 判定中：移动无效
    }

    private void LeaveToMove()
    {
        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else sm.ChangeState(sm.runState);
    }

    /// <summary>
    /// 【P1b 新增】攻击动画中按住 Ctrl 微调身位。
    /// </summary>
    private void TickBodyAdjust()
    {
        if (sm.sensor == null) return;

        // 带方向键的 Ctrl 是滑铲，不走这条路（由路由器处理）
        bool wantsAdjust = sm.playerController.isCrouchHeld
                           && Mathf.Abs(sm.playerController.moveInput.x) <= 0.1f;

        // 【修正】每帧重算目标形态，而不是只在按键状态变化时算一次。
        PlayerSensor.ColliderShape desired = PlayerSensor.ColliderShape.Normal;

        if (wantsAdjust)
        {
            desired = sm.IsGrounded()
                ? PlayerSensor.ColliderShape.Crouch
                : PlayerSensor.ColliderShape.TuckUp;
        }

        isBodyAdjusting = wantsAdjust;

        // 只有和当前形态不同才写，避免每帧重复设置碰撞体
        if (sm.sensor.CurrentShape != desired)
        {
            sm.sensor.SetColliderShape(desired);
        }
    }

    /// <summary>特殊招式（激光等）要求前摇和判定段里站住不动</summary>
    private bool HoldingStill
        => activeNode != null && activeNode.HoldsStillUntilRecovery
           && instance != null && instance.Phase != AttackPhase.Recovery;

    /// <summary>出招位移是否正在走</summary>
    private bool IsThrustActive => thrustRunning;

    /// <summary>
    /// 此刻要不要穿过敌人：这一招勾了 Pass Through Boss，且位移正在走（或这一物理帧就要起步）。
    /// 状态机在本状态的 FixedUpdate 之前问 —— 所以"这一帧就要起步"也要算上，否则第一步会撞上去。
    /// </summary>
    public bool WantsPassThrough
        => thrustRunning
            ? CurrentThrustPassThrough
            : (thrustPending && ReachedThrustStart() && activeNode != null && activeNode.passThroughDuringThrust);

    /// <summary>正在走的这段位移能不能穿身：拼刀位移看招式的 Clash Thrust Pass Through，出招位移看节拍总表</summary>
    private bool CurrentThrustPassThrough
        => clashThrustActive ? clashThrustPassThrough : (activeNode != null && activeNode.passThroughDuringThrust);

    /// <summary>
    /// 准备本招的位移冲量。
    /// </summary>
    private void SetupThrust()
    {
        thrustTimer = 0f;
        thrustDuration = 0f;
        thrustDistance = Vector2.zero;
        thrustPending = false;
        thrustRunning = false;
        thrustEndPending = false;
        thrustStartAt = 0f;

        if (activeNode == null) return;

        // 蓄力位移优先：它有自己的方向解析与动量加成
        if (activeNode.useAimDash)
        {
            SetupAimDash();
            return;
        }

        if (activeNode.forwardThrust.sqrMagnitude < 0.0001f) return;

        if (wasAirborneOnEnter && !activeNode.applyThrustInAir) return;

        float dir = sm.playerController.facingDirection;

        // X 乘朝向：填「前进多少」即可，不用管角色朝左朝右
        thrustDistance = new Vector2(
            dir * activeNode.forwardThrust.x,
            activeNode.forwardThrust.y);

        // 从所选阶段的开头起步；-1 = 和那个阶段一样长：阶段开始时起步，阶段结束时正好走到
        ThrustStartPhase phase = activeNode.thrustStart;
        thrustStartAt = PhaseStart(phase);
        thrustDuration = activeNode.thrustDuration >= 0f ? activeNode.thrustDuration : PhaseLength(phase);

        // 不在这里起步 —— 等那个阶段开始（TickThrust）
        thrustPending = true;
    }

    /// <summary>某个阶段从出招后第几秒开始</summary>
    private float PhaseStart(ThrustStartPhase phase)
    {
        if (instance == null) return 0f;
        switch (phase)
        {
            case ThrustStartPhase.Windup: return 0f;
            case ThrustStartPhase.Recovery: return instance.activeEnd;
            default: return instance.windupEnd;
        }
    }

    /// <summary>某个阶段的实际时长</summary>
    private float PhaseLength(ThrustStartPhase phase)
    {
        if (instance == null) return 0f;
        switch (phase)
        {
            case ThrustStartPhase.Windup: return Mathf.Max(0f, instance.windupEnd);
            case ThrustStartPhase.Recovery: return Mathf.Max(0f, instance.duration - instance.activeEnd);
            default: return Mathf.Max(0f, instance.activeEnd - instance.windupEnd);
        }
    }

    /// <summary>该起步了没有（没有攻击实例时立刻起步）</summary>
    private bool ReachedThrustStart()
        => instance == null || instance.elapsed >= thrustStartAt;

    /// <summary>
    /// 【蓄力位移】AA1 / BB1 这类"带位移的蓄力攻击"。
    /// </summary>
    private void SetupAimDash()
    {
        Vector2 dir = ResolveAimDashDirection();

        // ==========================================================
        // 【负数 = 反向位移（后坐力）】
        // ==========================================================
        float signedDistance = activeNode.aimDashDistance;
        float sign = signedDistance < 0f ? -1f : 1f;
        float distance = Mathf.Abs(signedDistance);

        // 朝向跟【瞄准】走，不跟位移走 —— 打后坐力时角色仍然面朝鼠标，
        // 只是身体被弹开。若跟着位移方向翻，开一枪人就背过身去了。
        Vector2 facingRef = dir;

        dir *= sign;

        // 贴着地面往下冲是没有意义的 —— 地面就在那儿，冲不进去
        if (IsGroundedDownwardDash(dir))
        {
            HandleGroundedDownwardDash(facingRef);
            return;
        }

        // -1 = 和攻击段一样长
        float d = activeNode.aimDashDuration;
        thrustDuration = d >= 0f ? Mathf.Max(0.01f, d) : PhaseLength(ThrustStartPhase.Active);
        thrustStartAt = PhaseStart(ThrustStartPhase.Active);   // 蓄力位移固定从判定段开始

        // 和普通出招位移一样：判定段开始时起步，匀速走完 distance
        thrustDistance = dir * distance;
        thrustTimer = 0f;
        thrustPending = true;

        // 诊断：看不到这条日志 = 本文件没有被导入到工程里
        if (Buffer != null && Buffer.verboseLog)
        {
            PlayerLog.Info(
                $"[蓄力位移] {activeNode.nodeName} 方向 {dir}，" +
                $"距离 {distance:F2}，耗时 {thrustDuration:F2}s（判定段开始时起步）");
        }

        // 朝向对齐瞄准方向，避免"往左冲却面朝右"。
        // 正数位移时 facingRef == dir；负数（后坐力）时它仍指向鼠标。
        if (Mathf.Abs(facingRef.x) > 0.01f)
            sm.playerController.SetFacingDirection(facingRef.x > 0f ? 1 : -1);
    }

    /// <summary>
    /// 按节点配置的自由度解析瞄准方向。
    /// </summary>
    private Vector2 ResolveAimDashDirection()
    {
        Vector2 facing = new Vector2(sm.playerController.facingDirection, 0f);

        // 明确要求只朝正面
        if (activeNode.aimDashMode == AimDashMode.FacingOnly)
        {
            LogAimDash("FacingOnly", "面朝方向", Vector2.zero, facing);
            return facing;
        }

        // ==========================================================
        // 【方向来源由武器类型决定，AimDashMode 只管吸附精度】
        // ==========================================================
        bool isRanged = ownerWeapon != null && ownerWeapon.IsRanged;

        Vector2 raw;
        string sourceName;

        if (isRanged)
        {
            if (AimProvider == null || !AimProvider.TryGetPointerDirection(out raw))
            {
                LogAimDash(activeNode.aimDashMode.ToString(), "远程·拿不到鼠标→面朝方向", Vector2.zero, facing);
                return facing;
            }
            sourceName = "远程·鼠标";
        }
        else
        {
            raw = sm.playerController.moveInput;
            sourceName = "近战·方向键";

            // 没按方向键 → 朝当前面朝方向。
            // 这是最常见的情况（专心蓄力时手指往往已经离开方向键），
            // 必须有确定的落点，不能让这招时灵时不灵。
            if (raw.sqrMagnitude < DirectionDeadzone * DirectionDeadzone)
            {
                LogAimDash(activeNode.aimDashMode.ToString(), "近战·无方向键→面朝方向", Vector2.zero, facing);
                return facing;
            }
        }

        Vector2 result;

        switch (activeNode.aimDashMode)
        {
            case AimDashMode.Free: result = raw.normalized; break;
            case AimDashMode.EightWay: result = SnapDirection(raw, 45f); break;
            case AimDashMode.FourWay: result = SnapDirection(raw, 90f); break;
            default: result = facing; break;
        }

        LogAimDash(activeNode.aimDashMode.ToString(), sourceName, raw, result);
        return result;
    }

    /// <summary>方向键按到多大才算「有方向」。与路由器的 directionThreshold 同量级</summary>
    private const float DirectionDeadzone = 0.1f;

    /// <summary>本次位移是不是「站在地上还要往下冲」</summary>
    private bool IsGroundedDownwardDash(Vector2 dir)
        => sm.IsGrounded() && dir.y < GroundedDownwardThreshold;

    /// <summary>
    /// 【扩展点】贴地向下冲的处理。
    /// </summary>
    private void HandleGroundedDownwardDash(Vector2 facingRef)
    {
        thrustDistance = Vector2.zero;
        thrustDuration = 0f;
        thrustTimer = 0f;
        thrustPending = false;

        // 朝向仍然对齐瞄准，保持和正常位移一致
        if (Mathf.Abs(facingRef.x) > 0.01f)
            sm.playerController.SetFacingDirection(facingRef.x > 0f ? 1 : -1);

        if (Buffer != null && Buffer.verboseLog)
            PlayerLog.Info("[蓄力位移] 在地面上向下冲 → 本次不位移（招式照常打出）");
    }

    /// <summary>
    /// 「贴地向下冲」的判定阈值。
    /// </summary>
    private const float GroundedDownwardThreshold = -0.9f;

    /// <summary>
    /// 【诊断】把吸附前后的角度都打出来。
    /// </summary>
    private void LogAimDash(string mode, string source, Vector2 raw, Vector2 snapped)
    {
        if (Buffer == null || !Buffer.verboseLog) return;

        float rawAngle = raw.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg
            : float.NaN;
        float snappedAngle = Mathf.Atan2(snapped.y, snapped.x) * Mathf.Rad2Deg;

        PlayerLog.Info(
            $"[蓄力位移·方向] 来源={source}　模式={mode}　" +
            $"原始角度={rawAngle:F1}°　吸附后={snappedAngle:F1}°");
    }

    /// <summary>把任意角度吸附到最近的 stepDegrees 倍数</summary>
    private static Vector2 SnapDirection(Vector2 dir, float stepDegrees)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = Mathf.Round(angle / stepDegrees) * stepDegrees;

        return new Vector2(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            Mathf.Sin(angle * Mathf.Deg2Rad));
    }

    /// <returns>本帧是否由位移接管了速度</returns>
    private bool TickThrust()
    {
        // 上一物理帧刚走完：把位移给的速度收掉，免得惯性滑出终点
        if (thrustEndPending)
        {
            thrustEndPending = false;
            StopThrustVelocity();
        }

        // 判定段开始 → 起步
        if (thrustPending && ReachedThrustStart())
        {
            thrustPending = false;
            thrustRunning = true;
            thrustTimer = 0f;
            thrustDuration = Mathf.Max(thrustDuration, Time.fixedDeltaTime);   // 填 0 也至少走一个物理帧
            clashThrustActive = false;
            thrustGuard.Begin(thrustDistance, thrustDistance.magnitude, thrustDuration,
                              activeNode != null && activeNode.passThroughDuringThrust);

            if (Buffer != null && Buffer.verboseLog && activeNode != null)
                PlayerLog.Info($"[出招位移] {activeNode.nodeName} 出招后 {thrustStartAt:F3}s 起步：位移 {thrustDistance}，用时 {thrustDuration:F3}s");
        }

        if (!thrustRunning) return false;

        // 匀速：每秒走 距离 ÷ 时长；最后一个物理帧只走剩下的那一点，保证总距离精确。
        // 终点裁判可能让这一步变短（停在 Boss 面前），或在时间到了之后继续走（穿到 Boss 身后）
        float dt = Time.fixedDeltaTime;
        float timeStep = Mathf.Max(0f, Mathf.Min(dt, thrustDuration - thrustTimer));
        thrustTimer += timeStep;

        float length = thrustDistance.magnitude;
        float step = thrustGuard.Step(length / thrustDuration * timeStep);
        ApplyThrustVelocity(length > 0.0001f ? thrustDistance / length * (step / dt) : Vector2.zero);

        if (thrustTimer >= thrustDuration - 0.00001f && thrustGuard.Reached)
        {
            thrustRunning = false;
            thrustEndPending = true;
            thrustGuard.End();
        }

        return true;
    }

    /// <summary>
    /// 【位移消弹】这一招勾了「位移」消弹时，位移进行期间（含走完后收速度的那一帧）
    /// 把压在芙兰身上的 Boss 子弹消掉，并让受伤入口把打过来的 Boss 子弹也消掉、不扣血。
    /// </summary>
    private void UpdateThrustBulletCancel()
    {
        bool on = (thrustRunning || thrustEndPending)
                  && activeNode != null && activeNode.CancelsBulletsBy(BulletCancelMode.Thrust);

        if (sm.playerState != null) sm.playerState.CancelBulletsOnContact = on;
        if (on) BulletCanceller.CancelOverlapping(sm.rb);
    }

    private void ApplyThrustVelocity(Vector2 v)
    {
        bool hasY = Mathf.Abs(thrustDistance.y) > 0.0001f;
        float vy = hasY ? v.y : sm.rb.linearVelocity.y;   // 没配 Y 就别干扰重力

        // 空中招式如果不带 Y 位移，保持原来的"锁死"语义，避免自由落体
        if (wasAirborneOnEnter && !hasY) vy = 0f;

        sm.rb.linearVelocity = new Vector2(v.x, vy);
    }

    /// <summary>
    /// 【拼刀位移】拼刀成功：立刻用出招位移那套逻辑走完一段精确距离（顶掉本招原来的位移）。
    /// 只在本状态还拥有这一招时生效。
    /// </summary>
    /// <param name="distance">世界方向的位移（已按朝向翻好）</param>
    /// <param name="passThrough">位移期间能不能穿过 Boss</param>
    /// <returns>是否接下了</returns>
    public bool StartClashThrust(AttackInstance inst, Vector2 distance, float duration, bool passThrough)
    {
        if (inst == null || inst != instance) return false;

        thrustDistance = distance;
        thrustDuration = Mathf.Max(duration, Time.fixedDeltaTime);
        thrustTimer = 0f;
        thrustPending = false;
        thrustRunning = true;
        thrustEndPending = false;
        clashThrustActive = true;
        clashThrustPassThrough = passThrough;
        thrustGuard.Begin(thrustDistance, thrustDistance.magnitude, thrustDuration, passThrough);

        if (Buffer != null && Buffer.verboseLog && activeNode != null)
            PlayerLog.Info($"[拼刀] {activeNode.nodeName} 拼刀位移：{distance}，用时 {thrustDuration:F3}s" + (passThrough ? "（可穿身）" : ""));
        return true;
    }

    /// <summary>把位移给的速度收掉（只收位移用到的方向）</summary>
    private void StopThrustVelocity()
    {
        Vector2 v = sm.rb.linearVelocity;
        if (Mathf.Abs(thrustDistance.x) > 0.0001f) v.x = 0f;
        if (Mathf.Abs(thrustDistance.y) > 0.0001f) v.y = 0f;
        sm.rb.linearVelocity = v;
    }

    public override void Exit()
    {
        // 【实验改动】这里不再碰判定、连射和手部后摇：
        //   · 判定 / 连射 / 特效归攻击实例管，动画被顶掉或取消后照常跑完（R5「只打断动画、不打断逻辑」）
        //   · 这只手锁到「前摇 + 攻击 + 后摇」走完，出招时就算好了，和动画何时结束无关
        //   · 该掐断判定的场合（冲刺/滑铲、前摇作废、受击）由 Buffer 的对应入口显式处理
        if (instance != null) instance.ownsAnimation = false;
        instance = null;

        // 攻击结束必须把碰撞箱还原，否则会带着蹲姿/缩腿姿态离开攻击状态
        if (isBodyAdjusting)
        {
            sm.sensor?.SetColliderShape(PlayerSensor.ColliderShape.Normal);
            isBodyAdjusting = false;
        }

        // 位移走到一半被打断（冲刺 / 受击 / 换手顶掉 / 后摇取消）：停在原地，不滑到终点
        if (thrustRunning || thrustEndPending) StopThrustVelocity();
        thrustPending = false;
        thrustRunning = false;
        thrustEndPending = false;
        thrustGuard.End();
        clashThrustActive = false;
        if (sm.playerState != null) sm.playerState.CancelBulletsOnContact = false;

        sm.rb.gravityScale = originalGravity;
        activeNode = null;
        ownerWeapon = null;
    }

    /// <summary>
    /// 超时未收到 OnAttackAnimationEnd。
    /// </summary>
    private void WarnAndExitOnTimeout()
    {
        if (!hasWarnedTimeout)
        {
            hasWarnedTimeout = true;

            string nodeName = activeNode != null ? activeNode.nodeName : "(未知招式)";
            string clipName = (activeNode != null && activeNode.attackClip != null)
                ? activeNode.attackClip.name
                : "(无剪辑)";

            Debug.LogWarning(
                $"[ComboState] 招式「{nodeName}」超过 {timeoutLimit:F2} 秒仍未收到 " +
                $"OnAttackAnimationEnd 事件，已强制退出。\n" +
                $"请检查动画「{clipName}」是否在最后一帧配置了该 Animation Event，" +
                "并确认它没有勾选 Loop Time。", sm);
        }

        // 走和正常收招一样的流程，保证连招宽恕期照常开始
        Buffer.StartGracePeriod();

        if (TryResumeFlight(force: true)) return;

        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else HandleLanding();
    }
}
