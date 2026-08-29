using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 连招执行状态。
///
/// ==========================================================
/// 【批次L 新增】动画事件兜底超时
///
/// 攻击的退出【只】依赖动画事件 OnAttackAnimationEnd。
/// 漏配一个事件 = 玩家永久卡在攻击动作里，而且【没有任何报错】——
/// 你只能靠猜是哪个招式、哪个动画出的问题。
///
/// 这次动画重做后 A1/B1 全部卡死，就是这个隐患的实证。
/// 后面还有十几个招式要配事件，漏一次就是一次卡死。
///
/// 比喻：办公室的门锁原本只靠"下班铃响了才开"。
///       现在加了个定时熄灯的总闸 —— 铃坏了人照样能走，
///       而且第二天保安会告诉你哪间屋的铃坏了。
///
/// 这和批次F 修连招指针泄漏是同一个思路：
/// 【动画事件应该是"锦上添花的时机点"，不能是"唯一的生命线"】。
/// 当时修的是连招指针，状态退出这条还留着同样的隐患，现在一并堵上。
/// ==========================================================
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

    // ---- 出招位移 ----
    private Vector2 thrustVelocity;
    private float thrustTimer;
    private float thrustDuration;

    // 碰撞箱微调是否生效中
    private bool isBodyAdjusting;

    /// <summary>
    /// 本招是哪只手打出的。
    ///
    /// 必须在 Enter 时就记下来 —— 因为换手抢拍时，
    /// 新招式的 SetCurrentNode 已经把 Buffer.LastTriggerCmd 改成新的那只手了，
    /// 等到旧招式 Exit 时再去问就问到错的手上，
    /// 后摇会记到刚出招的那只手头上，抢拍直接失效。
    /// </summary>
    private InputCmd ownerCmd;

    /// <summary>本次出招的编号。Exit 时用它核对"我还是不是最新的那一次"</summary>
    private int ownerAttackId;

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
    private PlayerMomentum momentum;

    public ComboState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    private PlayerWeaponEmitter WeaponEmitter
        => weaponEmitter != null ? weaponEmitter : (weaponEmitter = sm.GetComponent<PlayerWeaponEmitter>());

    private PlayerAimProvider AimProvider
        => aimProvider != null ? aimProvider : (aimProvider = sm.GetComponent<PlayerAimProvider>());

    private PlayerMomentum Momentum
        => momentum != null ? momentum : (momentum = sm.GetComponent<PlayerMomentum>());

    public override void Enter()
    {
        isCancelable = false;
        originalGravity = sm.rb.gravityScale;
        wasAirborneOnEnter = !sm.IsGrounded();

        isBodyAdjusting = false;
        activeNode = Buffer.currentNode;
        ownerCmd = Buffer.LastTriggerCmd;
        ownerAttackId = Buffer.GetHandAttackId(ownerCmd);

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

        timeoutLimit = (clipLength > 0f)
            ? clipLength * TimeoutMultiplier
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

        // 后摇取消检测
        if (isCancelable)
        {
            if (Buffer.TryAdvanceCombo()) return;
        }

        // 【P1b 改动】攻击动画中【不再】响应方向键转向。
        //
        // 原先允许用 a/d 微调朝向，但那会让玩家在挥刀途中来回转身，
        // 判定框跟着左右横跳，看起来像 bug。
        //
        // 新规则：攻击中只有【带方向键的冲刺/滑铲】能改变朝向 ——
        // 想转身就得付出一次位移的代价，不能白嫖。

        // ---- 碰撞箱微调（不打断攻击）----
        TickBodyAdjust();
    }

    public override void FixedUpdate()
    {
        // ---- 携带动量优先：滑铲途中出招时，滑行继续 ----
        //
        // 减速逻辑在 SlideMomentum 里，本状态只是"接手让它继续跑"，
        // 没有复制任何一行滑铲的物理参数。
        // ---- 出招位移优先 ----
        //
        // 【顺序修正】蓄力位移必须排在滑铲动量【之前】。
        // 否则滑铲后接 AA1 时，滑铲的余速会一直接管速度，
        // 蓄力位移永远轮不到执行 —— 而蓄力位移是玩家主动打出的、
        // 有明确方向意图的动作，理应压过被动的残余冲劲。
        if (TickThrust()) return;

        // ---- 携带动量：滑铲途中出招时，滑行继续 ----
        //
        // 减速逻辑在 SlideMomentum 里，本状态只是"接手让它继续跑"，
        // 没有复制任何一行滑铲的物理参数。
        bool wantsMomentum = activeNode == null || activeNode.inheritMomentum;

        if (wantsMomentum && sm.slideMomentum.Tick(sm, Time.fixedDeltaTime))
        {
            return;
        }

        // ---- 冲量结束后锁死移动 ----
        //
        // 【为什么必须主动写 0】
        // 原先地面上 ComboState 从来不写速度 —— 于是进入攻击那一刻的残留速度
        // （比如蓄力架势允许的 0.2 倍微移）会一路保持到攻击结束，
        // 表现出来就像"攻击期间还能走动"。
        //
        // 设计要求是「玩家攻击时默认都不能走动」，所以这里明确按住不放。
        if (sm.IsGrounded())
        {
            if (activeNode == null || activeNode.lockMovementDuringAttack)
            {
                sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
            }
            return;
        }

        // 空中连招期间锁死速度，不接受重力叠加
        sm.rb.linearVelocity = Vector2.zero;
        // 注意：位移期间 TickThrust 已经 return 了，走不到这里 ——
        // 所以空中的蓄力位移不会被这句抹掉
    }

    /// <summary>
    /// 【P1b 新增】攻击动画中按住 Ctrl 微调身位。
    ///
    /// 不打断攻击、不影响连段，只是把碰撞箱挪一点，用来躲擦边的攻击。
    ///   地面 → 蹲下，碰撞箱向下（躲头顶）
    ///   空中 → 缩腿，碰撞箱向上（躲脚下）
    ///
    /// 【为什么放在 ComboState 而不是路由器】
    /// 路由器负责"要不要切状态"，而这件事根本不切状态 ——
    /// 它是攻击状态内部的一个持续行为，跟着攻击一起开始、一起结束。
    /// 放路由器里的话，攻击结束时谁来还原碰撞箱就成了问题。
    /// </summary>
    private void TickBodyAdjust()
    {
        if (sm.sensor == null) return;

        // 带方向键的 Ctrl 是滑铲，不走这条路（由路由器处理）
        bool wantsAdjust = sm.playerController.isCrouchHeld
                           && Mathf.Abs(sm.playerController.moveInput.x) <= 0.1f;

        // 【修正】每帧重算目标形态，而不是只在按键状态变化时算一次。
        //
        // 原先是"按下时判断一次地面还是空中"，于是：
        //   按住 Ctrl 在地面攻击 → 应用 Crouch
        //   保持按住跳到空中     → 按键状态没变 → 不重新判断 → 还停在 Crouch
        // 反过来空中按住落地也一样，会停在 TuckUp。
        //
        // 环境是会变的，所以判断也必须跟着每帧走。
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

    /// <summary>出招冲量是否仍在生效</summary>
    private bool IsThrustActive => thrustDuration > 0f && thrustTimer < thrustDuration;

    /// <summary>
    /// 准备本招的位移冲量。
    ///
    /// 【为什么要衰减，而不是设一次速度就不管】
    /// 原先是 rb.linearVelocity = dir * forwardThrust.x 设完就走，
    /// 而 ComboState 期间没有任何东西会把它降下来 ——
    /// 结果是整个攻击动画期间角色匀速滑行，出招 0.5 秒就滑 0.5 秒，
    /// 像踩在冰上。
    ///
    /// 想要的是"往前一顿然后停下"，所以给一个持续时长，
    /// 速度在这段时间内线性衰减到 0。
    /// </summary>
    private void SetupThrust()
    {
        thrustTimer = 0f;
        thrustDuration = 0f;
        thrustVelocity = Vector2.zero;

        if (activeNode == null) return;

        // 蓄力位移优先：它有自己的方向解析与动量加成
        if (activeNode.useAimDash)
        {
            SetupAimDash();
            return;
        }

        if (activeNode.forwardThrust.sqrMagnitude < 0.0001f) return;

        // 带着滑铲动量进来时不覆盖它 ——
        // 否则"滑铲中出招"会在出招瞬间被拽回招式自己的速度，滑行断掉
        if (activeNode.inheritMomentum && sm.slideMomentum.IsActive) return;

        if (wasAirborneOnEnter && !activeNode.applyThrustInAir) return;

        float dir = sm.playerController.facingDirection;

        // X 乘朝向：填「前进多少」即可，不用管角色朝左朝右
        thrustVelocity = new Vector2(
            dir * activeNode.forwardThrust.x,
            activeNode.forwardThrust.y);

        thrustDuration = activeNode.thrustDuration;

        if (thrustDuration <= 0f)
        {
            // 兼容旧行为：设一次速度就不再管，整招匀速滑行
            ApplyThrustVelocity(1f);
        }
    }

    /// <summary>
    /// 【蓄力位移】AA1 / BB1 这类"带位移的蓄力攻击"。
    ///
    /// 与普攻的 forwardThrust 有两点本质不同：
    ///   ① 方向来自【瞄准】而不是角色朝向 —— 远程跟鼠标任意角度，近战四向吸附
    ///   ② 距离受【动量池】影响 —— 冲刺/滑铲后放能挪得更远
    ///
    /// 第二点是对"保持机动"的奖励：站桩蓄力只能拿低保距离，
    /// 而带着速度进蓄力再放，能挪出明显更远的身位。
    /// </summary>
    private void SetupAimDash()
    {
        Vector2 dir = ResolveAimDashDirection();

        float momentumValue = Momentum != null ? Momentum.Value : 0f;
        float distance = Mathf.Max(
            activeNode.aimDashBaseDistance,
            momentumValue * activeNode.aimDashMomentumScale);

        thrustDuration = Mathf.Max(0.01f, activeNode.aimDashDuration);

        // 位移速度按线性衰减积分反推：
        // 速度从 v 线性降到 0、历时 t，走过的距离是 v*t/2，
        // 所以要走 distance 就得从 2*distance/t 起步。
        float speed = 2f * distance / thrustDuration;

        thrustVelocity = dir * speed;
        thrustTimer = 0f;

        // 诊断：看不到这条日志 = 本文件没有被导入到工程里
        if (Buffer != null && Buffer.verboseLog)
        {
            Debug.Log(
                $"[蓄力位移] {activeNode.nodeName} 方向 {dir}，" +
                $"距离 {distance:F2}（低保 {activeNode.aimDashBaseDistance}，" +
                $"动量 {momentumValue:F1}×{activeNode.aimDashMomentumScale}），" +
                $"耗时 {thrustDuration:F2}s");
        }

        // 位移方向即出招朝向，避免"往左冲却面朝右"
        if (Mathf.Abs(dir.x) > 0.01f)
            sm.playerController.SetFacingDirection(dir.x > 0f ? 1 : -1);
    }

    /// <summary>按节点配置的自由度解析瞄准方向</summary>
    private Vector2 ResolveAimDashDirection()
    {
        // 没挂瞄准组件时退化为朝向
        // 没挂瞄准组件、或明确要求只朝正面 → 用角色朝向
        if (AimProvider == null || activeNode.aimDashMode == AimDashMode.FacingOnly)
            return new Vector2(sm.playerController.facingDirection, 0f);

        Vector2 aim = AimProvider.AimDirection;
        if (aim.sqrMagnitude < 0.0001f)
            return new Vector2(sm.playerController.facingDirection, 0f);

        switch (activeNode.aimDashMode)
        {
            case AimDashMode.Free:
                return aim.normalized;

            case AimDashMode.EightWay:
                return SnapDirection(aim, 45f);

            case AimDashMode.FourWay:
                return SnapDirection(aim, 90f);

            default:
                return new Vector2(sm.playerController.facingDirection, 0f);
        }
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
        if (thrustDuration <= 0f) return false;
        if (thrustTimer >= thrustDuration) return false;

        thrustTimer += Time.fixedDeltaTime;

        // 线性衰减：1 → 0
        float t = Mathf.Clamp01(1f - thrustTimer / thrustDuration);
        ApplyThrustVelocity(t);

        return true;
    }

    private void ApplyThrustVelocity(float scale)
    {
        float vy = Mathf.Abs(thrustVelocity.y) > 0.0001f
            ? thrustVelocity.y * scale
            : sm.rb.linearVelocity.y;   // 没配 Y 就别干扰重力

        // 空中招式如果不带 Y 位移，保持原来的"锁死"语义，避免自由落体
        if (wasAirborneOnEnter && Mathf.Abs(thrustVelocity.y) <= 0.0001f) vy = 0f;

        sm.rb.linearVelocity = new Vector2(thrustVelocity.x * scale, vy);
    }

    public override void Exit()
    {
        // 招式结束 → 这只手开始算后摇。
        // 无论是正常收招、被换手抢拍、还是被打断，都走这一条路，
        // 所以不存在"某种结束方式漏了没开始后摇"的可能。
        Buffer?.BeginHandRecovery(
            ownerCmd,
            activeNode != null ? activeNode.recoveryTime : 0f,
            ownerAttackId);

        // 攻击结束必须把碰撞箱还原，否则会带着蹲姿/缩腿姿态离开攻击状态
        if (isBodyAdjusting)
        {
            sm.sensor?.SetColliderShape(PlayerSensor.ColliderShape.Normal);
            isBodyAdjusting = false;
        }

        HitDetection?.ForceStopHitbox();

        // 中断连射：否则玩家已经被击飞了，枪还在原地继续突突
        WeaponEmitter?.CancelBurst();

        sm.rb.gravityScale = originalGravity;
        activeNode = null;
    }

    /// <summary>
    /// 超时未收到 OnAttackAnimationEnd。
    ///
    /// 警告里【点名是哪个招式】—— 这是本机制最大的价值：
    /// 不用再一个个试，Console 直接告诉你哪个动画漏了事件。
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

        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else HandleLanding();
    }
}