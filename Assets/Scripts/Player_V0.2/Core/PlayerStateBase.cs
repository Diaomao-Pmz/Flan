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
        sm.rb.linearVelocity = new Vector2(
            moveDir * sm.moveSpeed * speedMultiplier,
            sm.rb.linearVelocity.y);
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

    /// <summary>按水平输入翻转朝向。输入接近 0 时保持原朝向</summary>
    protected void UpdateFacing(float moveDirX)
    {
        if (moveDirX < -0.01f) sm.playerController.SetFacingDirection(-1);
        else if (moveDirX > 0.01f) sm.playerController.SetFacingDirection(1);
    }
}