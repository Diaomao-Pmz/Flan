using UnityEngine;
using UnityEngine.InputSystem;
using Flandre.CombatSystem;

/// <summary>
/// 【虚拟手柄】—— 全项目唯一允许接触 Input System 的文件。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(ComboInputBuffer))]
[RequireComponent(typeof(PlayerCommandRouter))]
public class PlayerController : MonoBehaviour
{
    [Header("硬件与核心脑部引用")]
    public Rigidbody2D rb { get; private set; }
    public SpriteRenderer sr { get; private set; }
    public Animator anim { get; private set; }

    public PlayerStateMachine stateMachine;
    public LoadoutManager loadoutManager;
    public ComboInputBuffer inputBuffer { get; private set; }
    public PlayerCommandRouter commandRouter { get; private set; }
    private Coroutine blinkCoroutine;

    [Header("虚拟手柄信号 (Virtual Gamepad)")]
    public Vector2 moveInput { get; private set; }
    public int facingDirection = 1;

    /// <summary>
    /// 鼠标/指针的屏幕坐标。由 PlayerAimProvider 转成世界方向。
    /// </summary>
    public Vector2 screenAimPosition { get; private set; }

    /// <summary>
    /// 这台机器上到底有没有鼠标。
    /// </summary>
    public bool hasPointerDevice { get; private set; }

    /// <summary>
    /// 玩家【现在】用的是手柄还是鼠标。
    /// </summary>
    public bool isUsingGamepad { get; private set; }

    // ==========================================
    // 持续按压与蓄力状态记录区
    // ==========================================
    public bool isJumpHeld { get; private set; }
    public bool isCrouchHeld { get; private set; }
    public bool isFlyHeld { get; private set; }

    public bool isMainAttackHeld { get; private set; }
    public float mainAttackHoldTime { get; private set; }

    public bool isSubAttackHeld { get; private set; }
    public float subAttackHoldTime { get; private set; }

    [Header("点按 / 长按判定")]
    [Tooltip(
        "按住超过这个时长就判定为「长按」，直接进入蓄力（不打出普攻）。\n\n" +
        "代价：普攻会有同等时长的输入延迟 —— 这是「同一个键区分点按与长按」\n" +
        "必须付出的账，调小可以更跟手但更容易把长按误判成点按。")]
    public float attackHoldThreshold = 0.15f;

    // 本次按压是否已经作出「点按 / 长按」的判定。
    // 一次按压只判定一次，判定完就不再重复触发。
    private bool mainHoldResolved;
    private bool subHoldResolved;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();

        stateMachine = GetComponent<PlayerStateMachine>();
        loadoutManager = GetComponent<LoadoutManager>();
        inputBuffer = GetComponent<ComboInputBuffer>();
        commandRouter = GetComponent<PlayerCommandRouter>();
    }

    void Update()
    {
        PollPointer();
        PollDebugReviewRepeat();

        bool isHit = (stateMachine != null && stateMachine.currentState == stateMachine.hitState);

        // 蓄力秒表走字 (双轨独立计时)
        if (!isHit)
        {
            if (isMainAttackHeld) mainAttackHoldTime += Time.deltaTime;
            if (isSubAttackHeld) subAttackHoldTime += Time.deltaTime;

            ResolveHoldIfNeeded();
        }
    }

<<<<<<< Updated upstream
=======
    // ==========================================================
    // 【正式版整改 批次B】蓄力上限键改走 InputActions
    // 症状：试验版直接读 Mouse.current 的侧键，改不了键位，也接不了手柄
    // 根因：试验时为了方便整体回退，没有动 .inputactions
    // ==========================================================
    private InputAction chargeCapMainAction;
    private InputAction chargeCapSubAction;

    // 调试用：逐帧回看的按键（F5~F9）。本文件只负责读键并转发，回看逻辑在 CombatDebugPanel
    private static readonly string[] debugReviewActionNames =
    {
        "DebugReviewToggle", "DebugReviewPrevFrame", "DebugReviewNextFrame",
        "DebugReviewPrevChange", "DebugReviewNextChange",
    };
    private readonly InputAction[] debugReviewActions = new InputAction[5];

    /// <summary>逐帧回看的按键被按下。CombatDebugPanel 订阅</summary>
    public event System.Action<DebugReviewCommand> DebugReviewCommandPressed;

    void Start()
    {
        var playerInput = GetComponent<PlayerInput>();
        var actions = playerInput != null ? playerInput.actions : null;

        chargeCapMainAction = actions != null ? actions.FindAction("ChargeCapMain") : null;
        chargeCapSubAction = actions != null ? actions.FindAction("ChargeCapSub") : null;

        if (chargeCapMainAction != null) chargeCapMainAction.performed += OnChargeCapMainPerformed;
        if (chargeCapSubAction != null) chargeCapSubAction.performed += OnChargeCapSubPerformed;

        for (int i = 0; i < debugReviewActions.Length; i++)
        {
            debugReviewActions[i] = actions != null ? actions.FindAction(debugReviewActionNames[i]) : null;
            if (debugReviewActions[i] != null) debugReviewActions[i].performed += OnDebugReviewPerformed;
        }

        if (chargeCapMainAction == null || chargeCapSubAction == null)
        {
            PlayerLog.Warn(
                "[输入] 没找到 ChargeCapMain / ChargeCapSub 这两个 Action，蓄力上限键（鼠标侧键）不会生效。\n" +
                "请检查玩家身上的 PlayerInput 组件用的是不是 PlayerInputConfig，以及里面有没有这两个 Action。");
        }
    }

    void OnDestroy()
    {
        if (chargeCapMainAction != null) chargeCapMainAction.performed -= OnChargeCapMainPerformed;
        if (chargeCapSubAction != null) chargeCapSubAction.performed -= OnChargeCapSubPerformed;

        for (int i = 0; i < debugReviewActions.Length; i++)
        {
            if (debugReviewActions[i] != null) debugReviewActions[i].performed -= OnDebugReviewPerformed;
        }
    }

    // 调试翻帧键（上一帧 / 下一帧）按住不放时连续翻：先停顿一下，再每帧翻 1 帧，按久了每帧翻 5 帧
    private const float DebugRepeatDelay = 0.3f;
    private const float DebugRepeatFastAfter = 1.5f;
    private float debugRepeatHeldTime;
    private int debugRepeatIndex = -1;

    private void PollDebugReviewRepeat()
    {
        // 下标 1 = 上一帧，2 = 下一帧（见 debugReviewActionNames）
        int held = -1;
        for (int i = 1; i <= 2; i++)
        {
            if (debugReviewActions[i] != null && debugReviewActions[i].IsPressed()) { held = i; break; }
        }

        if (held != debugRepeatIndex)
        {
            debugRepeatIndex = held;
            debugRepeatHeldTime = 0f;
            return;
        }
        if (held < 0) return;

        // 回看时游戏是暂停的，必须用不受 timeScale 影响的时间
        debugRepeatHeldTime += Time.unscaledDeltaTime;
        if (debugRepeatHeldTime < DebugRepeatDelay) return;

        int steps = debugRepeatHeldTime < DebugRepeatFastAfter ? 1 : 5;
        for (int s = 0; s < steps; s++)
            DebugReviewCommandPressed?.Invoke((DebugReviewCommand)held);
    }

    private void OnDebugReviewPerformed(InputAction.CallbackContext ctx)
    {
        // 五个 Action 共用一个回调，靠名字认是哪一个；数组下标和 DebugReviewCommand 的顺序一致
        int index = System.Array.IndexOf(debugReviewActionNames, ctx.action.name);
        if (index >= 0) DebugReviewCommandPressed?.Invoke((DebugReviewCommand)index);
    }

    private void OnChargeCapMainPerformed(InputAction.CallbackContext ctx) => RaiseChargeCap(InputCmd.MainAttack);
    private void OnChargeCapSubPerformed(InputAction.CallbackContext ctx) => RaiseChargeCap(InputCmd.SubAttack);

    /// <summary>蓄力上限 +1（3 级之后回到 1）。只在该手蓄力中有效；受击硬直中不响应</summary>
    private void RaiseChargeCap(InputCmd cmd)
    {
        if (stateMachine == null || stateMachine.currentState == stateMachine.hitState) return;

        var charge = stateMachine.chargeSystem;
        if (charge != null) charge.RaiseLevelCap(cmd);
    }

>>>>>>> Stashed changes
    /// <summary>
    /// 每帧同步指针位置。
    /// </summary>
    private void PollPointer()
    {
        var mouse = Mouse.current;
        hasPointerDevice = (mouse != null);
        if (mouse == null) return;

        Vector2 pos = mouse.position.ReadValue();

        // 鼠标真的动了（不是 1 像素的抖动）→ 玩家回到鼠标了。
        // 阈值与 PlayerAimProvider 里原先那套判活一致。
        if ((pos - screenAimPosition).sqrMagnitude > 1f) isUsingGamepad = false;

        screenAimPosition = pos;
    }

    /// <summary>
    /// 【可选】如果你更想用 Input Actions 驱动指针（比如要支持触屏或手柄准星），
    /// 在资产里建一个 Pass Through / Vector2 的 Look 动作，绑到这个函数即可。
    /// </summary>
    public void OnLook(InputAction.CallbackContext ctx)
    {
        screenAimPosition = ctx.ReadValue<Vector2>();
    }

    // ==========================================
    // 位移指令 → 路由器
    // ==========================================

    public void OnMove(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();

        // 方向输入来自手柄 → 记下"玩家在用手柄"。
        if (ctx.control != null && ctx.control.device is Gamepad) isUsingGamepad = true;
    }

    /// <summary>跳跃键（Space）。飞行中按无效（路由器会拒绝）</summary>
    public void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isJumpHeld = true;
            commandRouter.OnCommand(InputCmd.Jump);
        }
        else if (ctx.canceled)
        {
            isJumpHeld = false;
        }
    }

    public void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) commandRouter.OnCommand(InputCmd.Dash);
    }

    /// <summary>
    /// 滑铲 / 下蹲（共用 C 键）。
    /// </summary>
    public void OnCrouchSlidePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isCrouchHeld = true;
            commandRouter.OnCommand(InputCmd.Crouch);
        }
        else if (ctx.canceled)
        {
            isCrouchHeld = false;
        }
    }

    /// <summary>
    /// 飞行键（左 Alt）。【按下即发】一次 InputCmd.Fly —— 它是个开关：
    ///   没在飞（地面、起跳中、下落中都算）→ 起飞
    ///   正在飞                           → 解除飞行，自然下落
    /// 裁决在 PlayerCommandRouter.TryToggleFly（蓝量、攻击/后摇闸门都在那里）。
    /// isFlyHeld 只是如实记录按键状态，目前没有逻辑依赖它。
    /// </summary>
    public void OnFlyPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isFlyHeld = true;
            commandRouter.OnCommand(InputCmd.Fly);
        }
        else if (ctx.canceled)
        {
            isFlyHeld = false;
        }
    }

    /// <summary>
    /// 主动技能键。
    /// </summary>
    public void OnActiveSkillPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) loadoutManager?.TriggerActiveSkill();
    }

    // ==========================================
    // 攻击指令 → 连招引擎
    // ==========================================

    /// <summary>
    /// 【P1a 重写】攻击键改为「点按 / 长按」二选一。
    /// </summary>
    public void OnMainAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isMainAttackHeld = true;
            mainAttackHoldTime = 0f;
            mainHoldResolved = false;
        }
        else if (ctx.canceled)
        {
            isMainAttackHeld = false;

            if (!mainHoldResolved)
            {
                // 还没到长按阈值就松手 → 点按 → 普攻
                mainHoldResolved = true;
                inputBuffer.OnAttackTap(InputCmd.MainAttack);
            }
            else
            {
                // 已判定为长按 → 交给连招引擎结算这次蓄力（蓄满就放，没蓄满就血本无归），
                // 顺带清掉预输入缓存，免得松手后又跑出来打一发普攻
                inputBuffer.OnAttackReleased(InputCmd.MainAttack);
            }
        }
    }

    public void OnSubAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isSubAttackHeld = true;
            subAttackHoldTime = 0f;
            subHoldResolved = false;
        }
        else if (ctx.canceled)
        {
            isSubAttackHeld = false;

            if (!subHoldResolved)
            {
                subHoldResolved = true;
                inputBuffer.OnAttackTap(InputCmd.SubAttack);
            }
            else
            {
                inputBuffer.OnAttackReleased(InputCmd.SubAttack);
            }
        }
    }

    /// <summary>
    /// 按住超过阈值时，判定为长按并通知连招引擎进入蓄力。
    /// 一次按压只判定一次。
    /// </summary>
    private void ResolveHoldIfNeeded()
    {
        if (isMainAttackHeld && !mainHoldResolved
            && mainAttackHoldTime >= attackHoldThreshold)
        {
            mainHoldResolved = true;
            inputBuffer.OnAttackHold(InputCmd.MainAttack);
        }

        if (isSubAttackHeld && !subHoldResolved
            && subAttackHoldTime >= attackHoldThreshold)
        {
            subHoldResolved = true;
            inputBuffer.OnAttackHold(InputCmd.SubAttack);
        }
    }

    // ==========================================
    // 杂项
    // ==========================================

    public void SetFacingDirection(int dir)
    {
        facingDirection = dir;

        // 用 Transform 旋转而非 flipX：所有子物体（特效、判定框）自动跟着翻
        if (dir == 1) transform.rotation = Quaternion.Euler(0, 0, 0);
        else if (dir == -1) transform.rotation = Quaternion.Euler(0, 180, 0);
    }

    // ==========================================
    // 角色透明度（无敌半透明 + 受击闪烁）
    // ==========================================

    /// <summary>闪烁的"隐身"那一拍</summary>
    private bool isBlinkHidden;

    private float InvulnerableAlpha
    {
        get
        {
            var ps = stateMachine != null ? stateMachine.playerState : null;
            return ps != null && ps.combatConfig != null ? ps.combatConfig.invulnerableAlpha : 0.8f;
        }
    }

    void LateUpdate()
    {
        if (sr == null) return;

        var ps = stateMachine != null ? stateMachine.playerState : null;
        bool invulnerable = ps != null && ps.health.isUntargetable;

        // 闪烁的隐身拍优先；否则无敌 → 半透明，不无敌 → 不透明
        float alpha = isBlinkHidden ? 0f : (invulnerable ? InvulnerableAlpha : 1f);

        if (!Mathf.Approximately(sr.color.a, alpha))
        {
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }

    public void StartBlink(float duration, float interval)
    {
        if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
        isBlinkHidden = false;   // 上一轮可能停在隐身拍上
        blinkCoroutine = StartCoroutine(BlinkRoutine(duration, interval));
    }

    /// <summary>
    /// 受击闪烁：在"隐身"与"正常"之间来回切。
    /// "正常"那一拍显示多透明由 LateUpdate 决定 —— 受击期间是无敌的，所以是半透明。
    /// </summary>
    private System.Collections.IEnumerator BlinkRoutine(float duration, float interval)
    {
        float elapsed = 0f;
        if (interval <= 0) interval = 0.1f;

        while (elapsed < duration)
        {
            isBlinkHidden = true;
            yield return new WaitForSeconds(interval);
            elapsed += interval;

            isBlinkHidden = false;
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        isBlinkHidden = false;
        blinkCoroutine = null;
    }
}
