using UnityEngine;
using UnityEngine.InputSystem;
using Flandre.CombatSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(ComboInputBuffer))]
public class PlayerController : MonoBehaviour
{
    [Header("硬件与核心脑部引用")]
    public Rigidbody2D rb { get; private set; }
    public SpriteRenderer sr { get; private set; }
    public Animator anim { get; private set; }

    public PlayerStateMachine stateMachine;
    public LoadoutManager loadoutManager;
    public ComboInputBuffer inputBuffer { get; private set; }

    [Header("仅保留：原始输入数据传递")]
    public Vector2 moveInput { get; private set; }
    public int facingDirection = 1;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();

        stateMachine = GetComponent<PlayerStateMachine>();
        loadoutManager = GetComponent<LoadoutManager>();
        inputBuffer = GetComponent<ComboInputBuffer>();
    }

    void Update()
    {
        // 1. 鼠标类指令
        if (Input.GetMouseButtonDown(0))
        {
            if (inputBuffer != null) inputBuffer.OnReceiveInput(InputCmd.LightAttack);
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (stateMachine != null) stateMachine.TryEnterRangeAttack();
        }

        // 2. 键盘类动作指令
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (inputBuffer != null) inputBuffer.OnReceiveInput(InputCmd.Jump);
        }

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            if (inputBuffer != null) inputBuffer.OnReceiveInput(InputCmd.Dash);
        }

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f)
            {
                if (inputBuffer != null) inputBuffer.OnReceiveInput(InputCmd.Slide);
            }
            else
            {
                if (stateMachine != null) stateMachine.ChangeState(stateMachine.crouchState);
            }
        }
    }

    // 持续轴向移动保留
    public void OnMove(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    public void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) inputBuffer.OnReceiveInput(InputCmd.Jump);
    }

    public void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) inputBuffer.OnReceiveInput(InputCmd.Dash);
    }

    public void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) inputBuffer.OnReceiveInput(InputCmd.LightAttack);
    }

    public void SetFacingDirection(int dir)
    {
        facingDirection = dir;
        if (sr != null) sr.flipX = (dir == -1);
    }
}