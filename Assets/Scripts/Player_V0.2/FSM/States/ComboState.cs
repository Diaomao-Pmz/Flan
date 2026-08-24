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

    // 兜底超时
    private float timeoutLimit;
    private float elapsed;
    private bool hasWarnedTimeout;
    private ComboNode activeNode;

    // 缓存引用，避免每帧 GetComponent
    private ComboInputBuffer buffer;
    private PlayerHitDetection hitDetection;
    private PlayerWeaponEmitter weaponEmitter;

    public ComboState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    private PlayerWeaponEmitter WeaponEmitter
        => weaponEmitter != null ? weaponEmitter : (weaponEmitter = sm.GetComponent<PlayerWeaponEmitter>());

    public override void Enter()
    {
        isCancelable = false;
        originalGravity = sm.rb.gravityScale;
        wasAirborneOnEnter = !sm.IsGrounded();

        activeNode = Buffer.currentNode;

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
                sm.anim.Play(activeNode.animName, 0, 0f);
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

        // 攻击中允许微调朝向（瞄准/修招式朝向）。
        //
        // 但【冲量期间不许转】—— 位移方向是出招那一刻定死的，
        // 转身后会出现"面朝左却继续往右滑"的诡异画面，
        // 玩家会误以为是按方向键产生了位移。
        if (!IsThrustActive)
        {
            float moveDir = sm.playerController.moveInput.x;
            if (Mathf.Abs(moveDir) > 0.1f)
            {
                UpdateFacing(moveDir);
            }
        }
    }

    public override void FixedUpdate()
    {
        // ---- 携带动量优先：滑铲途中出招时，滑行继续 ----
        //
        // 减速逻辑在 SlideMomentum 里，本状态只是"接手让它继续跑"，
        // 没有复制任何一行滑铲的物理参数。
        bool wantsMomentum = activeNode == null || activeNode.inheritMomentum;

        if (wantsMomentum && sm.slideMomentum.Tick(sm, Time.fixedDeltaTime))
        {
            return;
        }

        // ---- 出招位移：短促冲量 + 线性衰减 ----
        if (TickThrust()) return;

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
