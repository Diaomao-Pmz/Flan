using Flandre.CombatSystem;
using UnityEngine;

/// <summary>
/// 玩家状态机 —— 现在只做三件事：持有卡带、切换卡带、驱动卡带。
///
/// ==========================================================
/// 【批次D 改动】上帝对象终于瘦下来了
///
/// 搬出去的东西：
///   IsGrounded / CanStand / SetColliderHeight  → PlayerSensor
///   groundCheck / ceilingCheck / hurtboxCore / groundLayer / 碰撞体备份 → PlayerSensor
///   SpawnProjectile / projectilePrefab / firePoint → PlayerWeaponEmitter
///   Jump 与 Fall 重复的悬停蓄力逻辑 → HoverChargeHandler
///
/// 演进过程：452 行（初始） → 约 300 行（批次A/B/C） → 现在约 230 行
///
/// 剩下的仍占篇幅的是那一堆「转发窗口」（moveSpeed / jumpForce / …）。
/// 它们是当初为了不动 11 张卡带而搭的转寄条，现在可以逐步拆掉了，
/// 不过那属于纯机械替换，收益低、风险也低，不急。
///
/// 【为了不破坏现有调用，本文件保留三个转发方法】
///   IsGrounded() / CanStand() / SetColliderHeight() → 转发给 PlayerSensor
/// 这样 11 张卡带里的 sm.IsGrounded() 一行都不用改。
/// ==========================================================
/// </summary>
public class PlayerStateMachine : MonoBehaviour
{
    public IState currentState;

    public IdleState idleState;
    public RunState runState;
    public JumpState jumpState;
    public FallState fallState;
    public DashState dashState;
    public CrouchState crouchState;
    public SlideState slideState;
    public FlyState flyState;

    public ComboState comboState;
    public ChargeState chargeState;
    public HitState hitState;

    // TODO: 阶段性接入 —— 当前按要求维持「0血不死」，故不实例化
    // public DeadState deadState;

    [Header("组件引用 (自动获取)")]
    public Animator anim;
    public Rigidbody2D rb;
    public TrailRenderer dashTrail;
    public PlayerController playerController;
    public PlayerState playerState;
    public LoadoutManager loadout;
    public PlayerCommandRouter commandRouter;
    public PlayerSensor sensor;

    // ==========================================================
    // 转发窗口
    // ==========================================================
    private PlayerMovementConfig MoveCfg => playerState != null ? playerState.movementConfig : null;
    private PlayerCombatConfig CombatCfg => playerState != null ? playerState.combatConfig : null;

    public float moveSpeed => playerState.stats.moveSpeed.Value;
    public int maxJumps => playerState.stats.maxJumps.IntValue;
    public float jumpForce => playerState.stats.jumpForce.Value;
    public float dashSpeed => playerState.stats.dashSpeed.Value;

    public float minJumpVelocity => MoveCfg.minJumpVelocity;
    public float dashDuration => MoveCfg.dashDuration;
    public float slideStartSpeedMultiplier => MoveCfg.slideStartSpeedMultiplier;
    public float slideDeceleration => MoveCfg.slideDeceleration;
    public float crouchSpeedMultiplier => MoveCfg.slideToCrouchSpeedMultiplier;
    public float crouchMoveSpeedMultiplier => MoveCfg.crouchMoveSpeedMultiplier;
    public float hoverChargeTime => MoveCfg.hoverChargeTime;
    public float flyManaCostPerSecond => MoveCfg.flyManaCostPerSecond;
    public float flyCancelJumpForce => MoveCfg.flyCancelJumpForce;
    public float flySpeed => MoveCfg.flySpeed;

    public float hitStunDuration => CombatCfg.hitStunDuration;
    public Vector2 hitKnockbackForce => CombatCfg.hitKnockbackForce;
    public float blinkInterval => CombatCfg.blinkInterval;

    [Header("Jump Runtime")]
    public int jumpCount = 0;

    [Header("Dash / Slide 充能配置")]
    public ComboSkill dashSkill = new ComboSkill();
    public ComboSkill slideSkill = new ComboSkill();

    /// <summary>本次动作是否由「受身打断」触发。由路由器置位，问完宝石立刻清零</summary>
    [HideInInspector] public bool isBreakingHitStun = false;

    /// <summary>
    /// 【携带动量】滑铲的冲劲，可以跨状态传递给连招与蓄力。
    /// 只有滑铲/连招/蓄力会保留它，进入其他状态时由 ChangeState 统一清空。
    /// </summary>
    public readonly SlideMomentum slideMomentum = new SlideMomentum();

    /// <summary>上一个状态。用于「从哪来」这类判断</summary>
    public IState previousState { get; private set; }

    private ComboInputBuffer cachedInputBuffer;
    public ComboInputBuffer inputBuffer
    {
        get
        {
            if (cachedInputBuffer == null) cachedInputBuffer = GetComponent<ComboInputBuffer>();
            return cachedInputBuffer;
        }
    }

    // ---- 延迟切换机制 ----
    private bool isTransitioning = false;
    private IState pendingState = null;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = GetComponent<PlayerState>();
        loadout = GetComponent<LoadoutManager>();
        commandRouter = GetComponent<PlayerCommandRouter>();
        sensor = GetComponent<PlayerSensor>();
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        cachedInputBuffer = GetComponent<ComboInputBuffer>();

        if (playerState != null)
        {
            playerState.EnsureInitialized();
            dashSkill.BindStats(playerState.stats);
            slideSkill.BindStats(playerState.stats);
        }
        else Debug.LogError("[PlayerStateMachine] 找不到 PlayerState！", this);

        if (sensor == null) Debug.LogError("[PlayerSensor] 缺失！地面检测将永远返回 false。", this);
        if (loadout == null) Debug.LogError("[LoadoutManager] 缺失！宝石系统不会生效。", this);
        if (commandRouter == null) Debug.LogError("[PlayerCommandRouter] 缺失！跳/冲/铲都不会响应。", this);

        idleState = new IdleState(this);
        runState = new RunState(this);
        jumpState = new JumpState(this);
        fallState = new FallState(this);
        dashState = new DashState(this);
        crouchState = new CrouchState(this);
        slideState = new SlideState(this);
        flyState = new FlyState(this);

        comboState = new ComboState(this);
        chargeState = new ChargeState(this);
        hitState = new HitState(this);

        // TODO: 接入死亡时解除注释
        // deadState = new DeadState(this);

        if (dashTrail == null) dashTrail = GetComponentInChildren<TrailRenderer>();
    }

    void Start()
    {
        ChangeState(idleState);

        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit -= HandlePlayerHit;
            playerState.health.OnPlayerHit += HandlePlayerHit;

            // TODO: 接入死亡时解除注释
            // playerState.health.OnPlayerDeath -= HandlePlayerDeath;
            // playerState.health.OnPlayerDeath += HandlePlayerDeath;
        }
    }

    void Update()
    {
        if (IsGrounded() && currentState != dashState && currentState != jumpState)
        {
            jumpCount = 0;
        }

        if (currentState != dashState) dashSkill.UpdateTimeout();
        if (currentState != slideState) slideSkill.UpdateTimeout();

        currentState?.Update();
    }

    void FixedUpdate()
    {
        // 接口已就位，下一批开始把速度写入迁移进来
        currentState?.FixedUpdate();
    }

    /// <summary>
    /// 切换状态。
    /// 在 Enter() 内部被再次调用时，新请求会排队，
    /// 等当前 Enter() 完整跑完后才执行 —— Exit() 永远不会在 Enter() 中途被触发。
    /// </summary>
    public void ChangeState(IState newState)
    {
        if (newState == null) return;

        if (isTransitioning)
        {
            pendingState = newState;
            return;
        }

        isTransitioning = true;
        previousState = currentState;
        currentState?.Exit();
        currentState = newState;
        ClearMomentumIfNeeded();
        currentState.Enter();
        isTransitioning = false;

        int guard = 0;
        while (pendingState != null)
        {
            IState next = pendingState;
            pendingState = null;

            isTransitioning = true;
            previousState = currentState;
            currentState.Exit();
            currentState = next;
            ClearMomentumIfNeeded();
            currentState.Enter();
            isTransitioning = false;

            if (++guard > 8)
            {
                Debug.LogError("[状态机] 连续切换超过 8 次，疑似状态循环，已强制中断。", this);
                pendingState = null;
                break;
            }
        }
    }

    /// <summary>
    /// 【携带动量的唯一清除点】
    ///
    /// 只有滑铲/连招/蓄力三个状态会保留动量，
    /// 进入其他任何状态（跳跃、冲刺、受击、待机…）都自动清空。
    ///
    /// 集中在这里做，是为了不需要在每张卡带里各写一遍 —— 也就不可能漏。
    /// 以后新增状态时默认就是"清空"，这是更安全的默认值。
    /// </summary>
    private void ClearMomentumIfNeeded()
    {
        bool keeps = currentState == slideState
                  || currentState == comboState
                  || currentState == chargeState;

        if (!keeps) slideMomentum.Clear();
    }

    // ==========================================
    // 感官转发 (实现在 PlayerSensor，此处只转发，避免改动 11 张卡带)
    // ==========================================

    public bool IsGrounded() => sensor != null && sensor.IsGrounded();
    public bool CanStand() => sensor == null || sensor.CanStand();
    public void SetColliderHeight(bool isCrouching) => sensor?.SetColliderHeight(isCrouching);

    // ==========================================
    // 攻击 —— 动画事件入口
    // ==========================================

    public void OpenComboWindow()
    {
        if (currentState == comboState)
        {
            comboState.isCancelable = true;
            inputBuffer.TryAdvanceCombo();
        }
    }

    public void CloseComboWindow()
    {
        if (currentState == comboState) comboState.isCancelable = false;
    }

    public void OnAttackAnimationEnd()
    {
        if (currentState != comboState) return;

        ComboInputBuffer buffer = inputBuffer;
        buffer.StartGracePeriod();

        ComboNode lastNode = buffer.currentNode;
        bool isCurrentButtonHeld = false;

        if (lastNode != null)
        {
            if (lastNode.inputSequence.Contains(InputCmd.MainAttack))
                isCurrentButtonHeld = playerController.isMainAttackHeld;
            else if (lastNode.inputSequence.Contains(InputCmd.SubAttack))
                isCurrentButtonHeld = playerController.isSubAttackHeld;
        }

        if (isCurrentButtonHeld) ChangeState(chargeState);
        else if (Mathf.Abs(playerController.moveInput.x) > 0.1f) ChangeState(runState);
        else ChangeState(idleState);
    }

    // ==========================================
    // 击飞
    // ==========================================

    private void HandlePlayerHit(Vector2 knockbackDirection)
    {
        // 受击时清空预输入缓存，否则硬直结束后挨打前按的键会突然全部兑现
        commandRouter?.ClearBuffer();

        hitState.SetKnockbackForce(knockbackDirection);
        ChangeState(hitState);
        playerController.StartBlink(playerState.health.invulnerableDuration, blinkInterval);
    }

    // TODO: 接入死亡时解除注释
    // private void HandlePlayerDeath() => ChangeState(deadState);

    void OnDestroy()
    {
        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit -= HandlePlayerHit;
            // playerState.health.OnPlayerDeath -= HandlePlayerDeath;
        }
    }
}
