using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 飞行状态。
///
/// ==========================================================
/// 【本批新增】跃起阶段（fly 衍生）
///
/// 招式表：「打出 AAn 后点按 fly 键，跃起直接进入飞行状态。
///          跃起过程中可以调整人物朝向，但落点由按下 fly 键时的方向键决定。」
///
/// 所以飞行有两种进入方式，行为不同：
///   悬停蓄力进入（长按 Q）→ 原地起飞，没有跃起
///   蓄力招衍生（点按 Q）  → 先按指定方向跃出去一段，再转为正常操控
///
/// 跃起期间【方向键不改变位移】—— 落点在按键那一刻就定死了，
/// 这是设计明确要求的。但朝向仍然可以调，所以还是有操作空间。
/// ==========================================================
///
/// 【释放锁机制】requireFlyRelease 用来模拟旧输入系统的 GetKeyDown：
/// 一切入飞行就上锁，要求玩家先松开 Q 键，
/// 否则从悬停蓄力进来的那一瞬间会立刻被判定为「再次按下 → 取消飞行」。
/// </summary>
public class FlyState : PlayerStateBase
{
    private float originalGravity;
    private float manaAccumulator;
    private bool requireFlyRelease = false;

    // ---- 跃起阶段 ----
    private bool isLeaping;
    private float leapTimer;
    private Vector2 leapDirection;

    public FlyState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private PlayerMovementConfig Cfg
        => sm.playerState != null ? sm.playerState.movementConfig : null;

    /// <summary>
    /// 由 PlayerCommandRouter 在 fly 衍生时调用，必须在 ChangeState 之前。
    /// 传入的方向就是按键那一刻的方向输入。
    /// </summary>
    public void PrepareLeap(Vector2 direction)
    {
        var cfg = Cfg;
        Vector2 fallback = cfg != null ? cfg.flyLeapDefaultDirection : Vector2.up;

        leapDirection = direction.sqrMagnitude > 0.01f
            ? direction.normalized
            : fallback.normalized;

        isLeaping = true;
        leapTimer = cfg != null ? cfg.flyLeapDuration : 0.25f;
    }

    public override void Enter()
    {
        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
        sm.rb.gravityScale = 0f;
        manaAccumulator = 0f;
        sm.animDriver.SetBase(PlayerAnimHash.Fly);

        // 跃起进入时保留冲力；常规进入时清零
        if (!isLeaping) sm.rb.linearVelocity = Vector2.zero;

        requireFlyRelease = sm.playerController.isFlyHeld;
    }

    public override void Update()
    {
        // ---- 耗蓝 ----
        manaAccumulator += sm.flyManaCostPerSecond * Time.deltaTime;
        if (manaAccumulator >= 1f)
        {
            int cost = Mathf.FloorToInt(manaAccumulator);
            manaAccumulator -= cost;

            if (!sm.playerState.health.ConsumeMP(cost))
            {
                sm.ChangeState(sm.fallState);
                return;
            }
        }

        // ---- 跃起阶段：不接受取消，也不接受移动输入 ----
        if (isLeaping)
        {
            leapTimer -= Time.deltaTime;
            if (leapTimer <= 0f) isLeaping = false;

            // 跃起中仍可调整朝向（设计明确允许）
            UpdateFacing(sm.playerController.moveInput.x);
            return;
        }

        // ---- 主动取消飞行 ----
        if (requireFlyRelease)
        {
            if (!sm.playerController.isFlyHeld) requireFlyRelease = false;
        }
        else if (sm.playerController.isFlyHeld)
        {
            sm.ChangeState(sm.fallState);
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.flyCancelJumpForce);
            return;
        }

        UpdateFacing(sm.playerController.moveInput.x);
    }

    public override void FixedUpdate()
    {
        if (isLeaping)
        {
            float speed = Cfg != null ? Cfg.flyLeapSpeed : 14f;
            sm.rb.linearVelocity = leapDirection * speed;
            return;
        }

        // 八向移动。飞行时 X/Y 都由输入完全接管，不保留原速度
        Vector2 moveDir = sm.playerController.moveInput.normalized;
        sm.rb.linearVelocity = moveDir * sm.flySpeed;
    }

    public override void Exit()
    {
        sm.rb.gravityScale = originalGravity;

        // 必须清干净：否则下次用悬停蓄力进飞行时会莫名其妙先跃出去一段
        isLeaping = false;
        leapTimer = 0f;
    }
}