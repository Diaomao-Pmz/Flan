using UnityEngine;
using Flandre.CombatSystem;

public class SlideState : IState
{
    private PlayerStateMachine sm;
    private float currentSlideSpeed;
    private float slideDirection;
    private bool isFirstHit;
    private bool isRejected;

    private readonly int playerLayer = LayerMask.NameToLayer("Player");
    private readonly int invincibleLayer = LayerMask.NameToLayer("Invincible");

    public SlideState(PlayerStateMachine stateMachine) { sm = stateMachine; }

    public void Enter()
    {
        isRejected = false;
        sm.playerController.loadoutManager.ExecuteActionModifier(ActionType.Slide);

        if (!sm.slideSkill.CanExecute())
        {
            Debug.LogWarning("[Slide] CD或硬直中！拦截执行");
            isRejected = true;
            HandleFallback();
            return;
        }

        isFirstHit = (sm.slideSkill.currentCombo == 0);
        sm.slideSkill.Execute();

        // ==========================================
        // 【逻辑分流 1】：如果是 Relay 的第二段，强制触发传送并退出
        // ==========================================
        if (!isFirstHit && sm.isSlideRelay)
        {
            sm.transform.position = sm.slideAnchorPos;
            sm.rb.linearVelocity = Vector2.zero;
            Debug.Log("[Slide] Relay 传送触发！");

            isRejected = true;
            HandleFallback();
            return;
        }

        // ==========================================
        // 【逻辑分流 2】：常规滑铲逻辑 (包含首滑，以及 Echo 的二滑)
        // ==========================================
        if (isFirstHit && sm.isSlideRelay)
        {
            sm.slideAnchorPos = sm.transform.position;
            sm.gameObject.layer = invincibleLayer; // Relay滑铲开启无敌

            if (sm.anchorPrefab != null)
                sm.activeSlideAnchor = GameObject.Instantiate(sm.anchorPrefab, sm.slideAnchorPos, Quaternion.identity);
        }

        // ----- 正常滑铲的物理与视觉表现 -----
        Debug.Log("进入了：滑铲状态");
        sm.anim.Play("Flandre_Slide");

        sm.SetColliderHeight(true);
        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        currentSlideSpeed = sm.moveSpeed * sm.slideStartSpeedMultiplier;

        // 读取玩家真实的按键输入 (使用 Input System 存下来的值，或者 GetAxisRaw)
        float inputX = sm.playerController.moveInput.x;
        if (inputX == 0) inputX = Input.GetAxisRaw("Horizontal"); // 双保险读取

        if (Mathf.Abs(inputX) > 0.1f)
        {
            slideDirection = Mathf.Sign(inputX);

            // 为了防止“倒着滑”的视觉 Bug，强制让身体翻转过去匹配滑铲方向
            sm.playerController.SetFacingDirection(slideDirection > 0 ? 1 : -1);
        }
        else
        {
            slideDirection = sm.playerController.facingDirection;
        }
    }

    public void Update()
    {
        currentSlideSpeed -= sm.slideDeceleration * Time.deltaTime;
        sm.rb.linearVelocity = new Vector2(slideDirection * currentSlideSpeed, sm.rb.linearVelocity.y);

        float targetCrouchSpeed = sm.moveSpeed * sm.crouchSpeedMultiplier;

        if (currentSlideSpeed <= targetCrouchSpeed) sm.ChangeState(sm.crouchState);
        else if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
    }

    public void Exit()
    {
        if (isRejected) return;

        if (sm.isSlideRelay) sm.gameObject.layer = playerLayer;

        sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
    }

    private void HandleFallback()
    {
        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else if (Mathf.Abs(sm.rb.linearVelocity.x) > 0.1f || Input.GetAxisRaw("Horizontal") != 0) sm.ChangeState(sm.runState);
        else sm.ChangeState(sm.idleState);
    }
}