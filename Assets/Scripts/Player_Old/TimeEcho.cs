using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class TimeEcho : MonoBehaviour
{
    [Header("Input Actions (New Input System)")]
    public InputActionReference echoAction; // 绑定 Echo（Q）

    [Header("References")]
    public PlayerController playerController;
    public PlayerShooter playerShooter;
    public SpriteRenderer spriteRenderer;

    [Header("Echo Settings")]
    public float maxRecordTime = 1.0f;
    public bool disablePlayerControlDuringReplay = true;

    [Header("Physics")]
    public bool disableGravityDuringReplay = true;  // ★ 新增：回放期间禁用重力
    float originalGravityScale;
    bool gravityModifiedByEcho = false;

    struct Frame
    {
        public float t;
        public Vector2 vel;
        public bool flipX;
    }

    enum State { Idle, Recording, Replaying }
    State state = State.Idle;

    Rigidbody2D rb;

    float recordStartTime;
    float recordElapsed;

    readonly List<Frame> frames = new List<Frame>(256);

    readonly List<float> attackTimes = new List<float>(32);
    int replayAttackIndex;

    float replayDuration;
    float replayTime;
    int replayFrameIndex;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (playerShooter == null) playerShooter = GetComponent<PlayerShooter>();

        originalGravityScale = rb.gravityScale;
    }

    void OnEnable()
    {
        if (echoAction != null)
        {
            echoAction.action.Enable();
            echoAction.action.performed += OnEchoPressed;
        }

        if (playerShooter != null)
        {
            playerShooter.Fired += OnShooterFired;
        }
    }

    void OnDisable()
    {
        if (echoAction != null)
        {
            echoAction.action.performed -= OnEchoPressed;
            echoAction.action.Disable();
        }

        if (playerShooter != null)
        {
            playerShooter.Fired -= OnShooterFired;
        }
    }

    void OnShooterFired()
    {
        if (state != State.Recording) return;
        attackTimes.Add(recordElapsed);
    }

    void OnEchoPressed(InputAction.CallbackContext ctx)
    {
        if (state == State.Idle)
        {
            BeginRecord();
        }
        else if (state == State.Recording)
        {
            EndRecordAndReplay();
        }
    }

    void BeginRecord()
    {
        frames.Clear();
        attackTimes.Clear();

        recordStartTime = Time.time;
        recordElapsed = 0f;

        state = State.Recording;
    }

    void EndRecordAndReplay()
    {
        replayDuration = Mathf.Clamp(recordElapsed, 0f, maxRecordTime);

        if (replayDuration <= 0.02f || frames.Count < 2)
        {
            state = State.Idle;
            return;
        }

        replayTime = 0f;
        replayFrameIndex = 0;
        replayAttackIndex = 0;

        if (disablePlayerControlDuringReplay)
            SetPlayerControlEnabled(false);

        // ★★ 回放阶段禁用重力 ★★
        if (disableGravityDuringReplay && rb != null)
        {
            if (!gravityModifiedByEcho)
            {
                originalGravityScale = rb.gravityScale;
                gravityModifiedByEcho = true;
            }
            rb.gravityScale = 0f;
        }

        state = State.Replaying;
    }

    void Update()
    {
        if (state == State.Recording)
        {
            recordElapsed = Time.time - recordStartTime;

            if (recordElapsed >= maxRecordTime)
            {
                recordElapsed = maxRecordTime;
                EndRecordAndReplay();
            }
        }
        else if (state == State.Replaying)
        {
            replayTime += Time.deltaTime;

            while (replayAttackIndex < attackTimes.Count && attackTimes[replayAttackIndex] <= replayTime)
            {
                if (playerShooter != null) playerShooter.Fire();
                replayAttackIndex++;
            }

            if (replayTime >= replayDuration)
            {
                FinishReplay();
            }
        }
    }

    void FixedUpdate()
    {
        if (state == State.Recording)
        {
            Frame f = new Frame
            {
                t = recordElapsed,
                vel = rb.linearVelocity,
                flipX = (spriteRenderer != null) ? spriteRenderer.flipX : false
            };
            frames.Add(f);
        }
        else if (state == State.Replaying)
        {
            if (frames.Count == 0) return;

            while (replayFrameIndex < frames.Count - 1 && frames[replayFrameIndex + 1].t <= replayTime)
                replayFrameIndex++;

            Frame cur = frames[replayFrameIndex];

            if (spriteRenderer != null)
                spriteRenderer.flipX = cur.flipX;

            rb.linearVelocity = cur.vel;
        }
    }

    void FinishReplay()
    {
        state = State.Idle;

        rb.linearVelocity = Vector2.zero;

        // ★★ 回放结束恢复重力 ★★
        if (disableGravityDuringReplay && rb != null && gravityModifiedByEcho)
        {
            rb.gravityScale = originalGravityScale;
            gravityModifiedByEcho = false;
        }

        if (disablePlayerControlDuringReplay)
            SetPlayerControlEnabled(true);
    }

    void SetPlayerControlEnabled(bool enabled)
    {
        if (playerController != null)
            playerController.enabled = enabled;

        if (playerShooter != null)
            playerShooter.allowInput = enabled;
    }
}
