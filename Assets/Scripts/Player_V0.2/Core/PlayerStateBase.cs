using UnityEngine;

/// <summary>
/// 【状态卡带基类】
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
    /// </summary>
    protected void ApplyHorizontalMove(float speedMultiplier = 1f)
    {
        float moveDir = sm.playerController.moveInput.x;
        float speed = sm.moveSpeed * speedMultiplier;

<<<<<<< Updated upstream
        // 【蓄力时的速度继承】动量充当一个逐渐衰减的速度下限。
        //
        // 冲刺后立刻进蓄力 → 起步接近冲刺速度 → 随动量衰减自然滑落到折损速度。
        // 表现是「带着冲劲进蓄力还能滑一小段」，而不是一进蓄力就急刹车。
        //
        // 取 max 而不是相加 —— 它是"你还剩多少冲劲"，不是额外加成。
        //
        // 它是配合"蓄力减速"的补偿，所以只在 chargeSlowsMovement 打开时生效；
        // 不减速时再套它，蓄力反而会比平时跑得快。
        var moveCfg = sm.playerState != null ? sm.playerState.movementConfig : null;
        bool chargeSlows = moveCfg != null && moveCfg.chargeSlowsMovement;

        if (chargeSlows && IsCharging && Momentum != null)
        {
            speed = Mathf.Max(speed, Momentum.GetChargeSpeedFloor());
        }
=======
        var chargeCfg = sm.playerState != null ? sm.playerState.combatConfig : null;
>>>>>>> Stashed changes

        // 【蓄力准备窗口】按住攻击键准备蓄力、人还在攻击动画/后摇里 → 地面移速打折
        if (IsInPreChargeWindow && sm.IsGrounded())
        {
            speed *= moveCfg != null ? moveCfg.preChargeMoveSpeedMultiplier : 0.5f;
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

    protected Flandre.CombatSystem.PlayerChargeSystem ChargeSystem
    {
        get
        {
            if (cachedCharge == null)
                cachedCharge = sm.GetComponent<Flandre.CombatSystem.PlayerChargeSystem>();
            return cachedCharge;
        }
    }

    /// <summary>有任意一只手正在蓄力</summary>
    protected bool IsCharging => ChargeSystem != null && ChargeSystem.IsAnyCharging;

    /// <summary>
    /// 攻击后摇是否正在锁住移动。
    /// </summary>
    /// <summary>处于蓄力准备窗口（见 ComboInputBuffer.IsInPreChargeWindow）</summary>
    protected bool IsInPreChargeWindow
    {
        get
        {
            if (cachedBuffer == null) cachedBuffer = sm.GetComponent<ComboInputBuffer>();
            return cachedBuffer != null && cachedBuffer.IsInPreChargeWindow;
        }
    }

    protected bool IsMovementLockedByAttack
    {
        get
        {
            if (cachedBuffer == null) cachedBuffer = sm.GetComponent<ComboInputBuffer>();
            // 蓄力准备窗口内放行移动（移速在 ApplyHorizontalMove 里打折）。
            // 只放行"走"：跳跃、起飞在后摇里仍然被路由器挡住，后摇的代价不会被跳掉。
            return cachedBuffer != null
                && cachedBuffer.IsMovementLockedByRecovery()
                && !cachedBuffer.IsInPreChargeWindow;
        }
    }

    /// <summary>按水平输入翻转朝向。输入接近 0 时保持原朝向</summary>
    protected void UpdateFacing(float moveDirX)
    {
        if (moveDirX < -0.01f) sm.playerController.SetFacingDirection(-1);
        else if (moveDirX > 0.01f) sm.playerController.SetFacingDirection(1);
    }
}