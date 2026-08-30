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

    // 【已删除】IsAirMoveLockedByCharge —— 曾经的判据"在蓄力 且 在空中"。
    // 它把两种完全不同的处境混成了一个条件（见下方 IsAirChargePinned 的说明），
    // 标了 Obsolete 之后全项目已无任何引用，本次一并删除。

    /// <summary>
    /// 本帧是否该把角色钉在空中悬停。
    ///
    /// ==========================================================
    /// 【判据是"蓄力在哪起手"，不是"现在在空中"、也不是"现在是升是降"】
    ///
    /// 这条判据前后错过两次，两次都是同一个病：用一个【每帧都在变的量】
    /// 去表达一个【这次蓄力的固有性质】。
    ///
    /// 错法一："在蓄力 且 在空中"（旧的 IsAirMoveLockedByCharge）。
    ///   它分不清"空中起手蓄力"（玩家主动做出的高风险承诺，该受限制）和
    ///   "地面起手蓄力后跳起来"（只是一次普通的折损跳跃，凭什么也被限制）。
    ///   症状：蓄力时跳起来只能直上直下。
    ///
    /// 错法二：再加一个 rb.velocity.y <= 0，想着"上升段放行，免得起跳冲量被抹掉"。
    ///   冲量问题确实解决了，但这个判据表达的是【这一帧在升还是在降】。
    ///   症状：地面蓄力起跳 → 上升正常 → 一到顶点就被钉住，跳到一半卡在空中。
    ///
    /// 现在换成起手位置（BeganAirborne）：
    ///   地面起手 → 永不钉。跳跃是完整的折损弧线，起跳-到顶-落下。
    ///   空中起手 → 全程钉住。这才是「空中蓄力是高风险承诺」，
    ///              想挪位置只能花一次冲刺。
    ///
    /// 起手位置在整段蓄力里是常量，所以不会再出现"某一帧忽然变规则"。
    /// ==========================================================
    /// </summary>
    protected bool IsAirChargePinned
        => ChargeSystem != null && ChargeSystem.IsAirChargeHovering;

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
    /// 【空中蓄力：完全钉在原地】速度清零 + 重力清零。
    ///
    /// ==========================================================
    /// 【为什么不能用「钳制下落速度」做】
    ///
    /// 上一版是 if (v.y < -maxFall) v.y = -maxFall，把 maxFall 填 0 本该停住，
    /// 实际却仍在缓降。原因是执行顺序：
    ///
    ///     我们的 FixedUpdate 把 v.y 钳成 0
    ///       → 物理引擎接着步进：先加一帧重力（v.y 变成 -g·dt），再按这个速度位移
    ///
    /// 也就是每个物理步都有【一帧的重力】从钳位后面漏过去，
    /// 积累起来就是肉眼可见的缓降。钳位永远追不上重力，因为它跑在重力前面。
    ///
    /// 比喻：每天早上把水桶倒空，但白天一直在滴水 ——
    ///       倒得再干净，到晚上还是有水。要治得关掉水龙头。
    ///
    /// 所以正确做法是【关掉重力源】：gravityScale = 0。
    /// 这也正是解耦前 ChargeState 里那句「空中蓄力反重力钉在原地」，
    /// 拆成随身模块时丢掉了，现在补回来。
    /// ==========================================================
    ///
    /// 顺带解决第二个症状：带着水平速度落下时进蓄力，角色会一直朝那个方向匀速飘。
    /// 因为原先只是「不写速度」，而 2D 刚体没有阻力，不写 = 保持原速。
    /// 现在明确清零，蓄力起手那一刻就完全静止。
    /// </summary>
    protected void PinInAirWhileCharging()
    {
        sm.rb.gravityScale = 0f;
        sm.rb.linearVelocity = Vector2.zero;
    }

    /// <summary>按水平输入翻转朝向。输入接近 0 时保持原朝向</summary>
    protected void UpdateFacing(float moveDirX)
    {
        if (moveDirX < -0.01f) sm.playerController.SetFacingDirection(-1);
        else if (moveDirX > 0.01f) sm.playerController.SetFacingDirection(1);
    }
}