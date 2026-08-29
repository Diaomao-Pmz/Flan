using UnityEngine;

/// <summary>
/// 【状态卡带基类】
///
/// ==========================================================
/// 【批次E 改动】明确职责边界
///
///   Update()      → 逻辑判断：该不该切状态、播哪个动画、朝向翻转、计时器
///   FixedUpdate() → 物理写入：rb.linearVelocity = ...
///
/// 为什么必须分开：
/// Unity 的物理固定每秒步进 50 次，而 Update 的频率随帧率浮动。
/// 速度写在 Update 里时 ——
///   144Hz：一个物理步之间写了近 3 次，只有最后一次算数
///   30fps：一次写要撑过 1~2 个物理步，中间那些步用的是过期数据
///
/// 比喻：物理引擎每 20 毫秒准时来收一次快递。
///       写在 Update 里 = 想起来就往门口放一个，
///       有时一个周期放三个（只收走最后一个），有时一个都没放。
///       改完之后是掐着点每次放一个。
///
/// 后果就是跳跃高度、冲刺距离在不同帧率下有细微差异，掉帧时手感发飘。
/// ==========================================================
/// </summary>
public abstract class PlayerStateBase : IState
{
    protected readonly PlayerStateMachine sm;

    protected PlayerStateBase(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public virtual void Enter() { }

    /// <summary>渲染帧：逻辑判断、动画切换、状态流转、朝向</summary>
    public virtual void Update() { }

    /// <summary>物理帧：速度写入</summary>
    public virtual void FixedUpdate() { }

    public virtual void Exit() { }

    // ==========================================================
    // 物理工具（供 FixedUpdate 使用）
    // ==========================================================

    /// <summary>
    /// 按当前方向输入写入水平速度，保留 Y 轴（不干扰重力/跳跃抛物线）。
    ///
    /// 地面跑动、空中控制、蹲行、蓄力微移全都用这一个方法，
    /// 差别只在倍率上 —— 以后想给所有移动加一个全局系数（比如减速 debuff），
    /// 改这一处就够了。
    /// </summary>
    protected void ApplyHorizontalMove(float speedMultiplier = 1f)
    {
        float moveDir = sm.playerController.moveInput.x;
        float speed = sm.moveSpeed * speedMultiplier;

        // 【蓄力时的速度继承】动量充当一个逐渐衰减的速度下限。
        //
        // 冲刺后立刻进蓄力 → 起步接近冲刺速度 → 随动量衰减自然滑落到折损速度。
        // 表现是「带着冲劲进蓄力还能滑一小段」，而不是一进蓄力就急刹车。
        //
        // 取 max 而不是相加 —— 它是"你还剩多少冲劲"，不是额外加成。
        if (IsCharging && Momentum != null)
        {
            speed = Mathf.Max(speed, Momentum.GetChargeSpeedFloor());
        }

        sm.rb.linearVelocity = new Vector2(moveDir * speed, sm.rb.linearVelocity.y);
    }

    // ==========================================================
    // 流转工具（供 Update 使用）
    // ==========================================================

    /// <summary>
    /// 动作结束后的通用回退：还在空中就下落，有横向速度或方向输入就跑，否则待机。
    /// 原先 DashState 和 SlideState 里各有一份逐字相同的实现。
    /// </summary>
    protected void HandleFallback()
    {
        float moveInputX = sm.playerController.moveInput.x;

        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
        }
        else if (Mathf.Abs(sm.rb.linearVelocity.x) > 0.1f || Mathf.Abs(moveInputX) > 0.1f)
        {
            sm.ChangeState(sm.runState);
        }
        else
        {
            sm.ChangeState(sm.idleState);
        }
    }

    /// <summary>落地后的去向：有方向输入就跑，否则待机</summary>
    protected void HandleLanding()
    {
        if (Mathf.Abs(sm.playerController.moveInput.x) > 0.1f) sm.ChangeState(sm.runState);
        else sm.ChangeState(sm.idleState);
    }

    // 缓存，避免每帧 GetComponent
    private ComboInputBuffer cachedBuffer;
    private Flandre.CombatSystem.PlayerChargeSystem cachedCharge;
    private Flandre.CombatSystem.PlayerMomentum cachedMomentum;

    protected Flandre.CombatSystem.PlayerChargeSystem ChargeSystem
    {
        get
        {
            if (cachedCharge == null)
                cachedCharge = sm.GetComponent<Flandre.CombatSystem.PlayerChargeSystem>();
            return cachedCharge;
        }
    }

    protected Flandre.CombatSystem.PlayerMomentum Momentum
    {
        get
        {
            if (cachedMomentum == null)
                cachedMomentum = sm.GetComponent<Flandre.CombatSystem.PlayerMomentum>();
            return cachedMomentum;
        }
    }

    /// <summary>有任意一只手正在蓄力</summary>
    protected bool IsCharging => ChargeSystem != null && ChargeSystem.IsAnyCharging;

    /// <summary>
    /// 空中蓄力时禁止方向键移动。
    ///
    /// 设计意图：空中蓄力是一次高风险承诺 —— 不能自由飘，
    /// 想调整位置只能花一次冲刺（Shift 朝鼠标方向冲）。
    /// 地面蓄力不受此限，仍可减速移动。
    /// </summary>
    protected bool IsAirMoveLockedByCharge => IsCharging && !sm.IsGrounded();

    /// <summary>
    /// 攻击后摇是否正在锁住移动。
    ///
    /// 放在基类是因为地面移动相关的卡带（Idle / Run）都要查它，
    /// 以后如果蹲行、滑铲也要遵守同一条规则，直接用就行。
    /// </summary>
    protected bool IsMovementLockedByAttack
    {
        get
        {
            if (cachedBuffer == null) cachedBuffer = sm.GetComponent<ComboInputBuffer>();
            return cachedBuffer != null && cachedBuffer.IsMovementLockedByRecovery();
        }
    }

    /// <summary>
    /// 空中蓄力时限制下落速度。
    ///
    /// 【为什么需要】解耦前 ChargeState 有一句「空中蓄力把角色钉在原地」，
    /// 那是状态独有的行为，拆成模块后丢了。
    ///
    /// 而蓄力一段通常要 1 秒左右，从跳跃最高点落地往往不到 1 秒 ——
    /// 不限速的话，空中蓄力几乎不可能在落地前完成，
    /// 玩家会以为"空中根本不能蓄力"。
    ///
    /// 用【钳制最大下落速度】而不是每帧乘一个系数：
    /// 后者的效果会随帧率变化，前者不会。
    /// </summary>
    protected void ClampAirChargeFall()
    {
        if (!IsAirMoveLockedByCharge || ChargeSystem == null) return;

        float maxFall = ChargeSystem.airChargeMaxFallSpeed;
        Vector2 v = sm.rb.linearVelocity;

        if (v.y < -maxFall)
        {
            sm.rb.linearVelocity = new Vector2(v.x, -maxFall);
        }
    }

    /// <summary>按水平输入翻转朝向。输入接近 0 时保持原朝向</summary>
    protected void UpdateFacing(float moveDirX)
    {
        if (moveDirX < -0.01f) sm.playerController.SetFacingDirection(-1);
        else if (moveDirX > 0.01f) sm.playerController.SetFacingDirection(1);
    }
}