using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

public class ComboInputBuffer : MonoBehaviour
{
    [Header("连招树根节点 (起手招式)")]
    public List<ComboNode> rootNodes = new List<ComboNode>();
    public ComboNode currentNode { get; private set; }

    [Header("工业级 ACT 手感配置")]
    [Tooltip("预输入缓存时间：玩家提前多久按键能被系统记住")]
    public float bufferLifespan = 0.2f;

    // ==========================================
    // 预输入缓存核心变量 (现已支持蓄力时长)
    // ==========================================
    private bool hasBufferedInput = false;
    private InputCmd bufferedCmd;         // 缓存的指令 (如 LightAttack)
    private float bufferedHoldTime = 0f;  // 缓存的蓄力时间 (0代表瞬间按下，>0代表蓄力松开)
    private float bufferTimer = 0f;

    // 延迟派生宽限期
    private bool isGracePeriodActive = false;
    private float graceTimer = 0f;

    private PlayerStateMachine sm;
    private LoadoutManager loadout;

    void Awake()
    {
        sm = GetComponent<PlayerStateMachine>();
        loadout = GetComponent<LoadoutManager>();
    }

    void Update()
    {
        // 1. 预输入缓存倒计时
        if (hasBufferedInput)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0)
            {
                hasBufferedInput = false;
            }
        }

        // 2. 延迟派生宽限期倒计时
        if (isGracePeriodActive && currentNode != null)
        {
            graceTimer += Time.deltaTime;
            if (graceTimer > currentNode.comboWindow)
            {
                Debug.Log("[ComboBuffer] 连招宽限期超时，彻底断档！");
                ResetCombo();
            }
        }
    }

    // ==================================================
    // 接口 1：接收常规按下指令 (holdTime 默认为 0)
    // ==================================================
    public void OnReceiveInput(InputCmd cmd)
    {
        // ==================================================
        // 受身打断 (Combo Breaker)
        // ==================================================
        if (sm != null && sm.currentState == sm.hitState)
        {
            if (CheckIfHasShieldGem(cmd))
            {
                if (cmd == InputCmd.Jump) sm.ChangeState(sm.jumpState);
                if (cmd == InputCmd.Dash) sm.ChangeState(sm.dashState);
                return;
            }
            return;
        }

        // ==================================================
        // 纯位移动作类指令 - 立刻放行
        // ==================================================
        if (cmd == InputCmd.Jump || cmd == InputCmd.Dash)
        {
            if (cmd == InputCmd.Dash && sm.dashSkill.CanExecute()) sm.ChangeState(sm.dashState);
            if (cmd == InputCmd.Jump && sm.jumpCount < sm.maxJumps) sm.ChangeState(sm.jumpState);
            return;
        }

        // ==================================================
        // 攻击类指令 (主/副攻击同等地位) - 存入预输入缓存！
        // ==================================================
        if (cmd == InputCmd.MainAttack || cmd == InputCmd.SubAttack)
        {
            hasBufferedInput = true;
            bufferedCmd = cmd;
            bufferedHoldTime = 0f; // 瞬间按下
            bufferTimer = bufferLifespan;

            if (sm.currentState != sm.comboState) TryAdvanceCombo();
        }
    }

    // ==================================================
    // 接口 2：【新增】接收蓄力松开指令
    // ==================================================
    public void OnReceiveChargeRelease(InputCmd cmd, float holdTime)
    {
        hasBufferedInput = true;
        bufferedCmd = cmd;
        bufferedHoldTime = holdTime; // 记录蓄了多久
        bufferTimer = bufferLifespan;

        if (sm.currentState != sm.comboState) TryAdvanceCombo();
    }

    // ==================================================
    // 核心匹配引擎：寻找最合适的招式
    // ==================================================
    public bool TryAdvanceCombo()
    {
        if (!hasBufferedInput) return false;

        ComboNode match = null;
        if (currentNode == null)
        {
            // 在根节点中寻找起手式
            match = FindBestMatch(rootNodes, bufferedCmd, bufferedHoldTime);
        }
        else
        {
            // 在子节点中寻找派生式
            match = FindBestMatch(currentNode.childNodes, bufferedCmd, bufferedHoldTime);
        }

        if (match != null)
        {
            currentNode = match;
            ExecuteNode();
            return true;
        }

        return false;
    }

    // 【新增】：工业级指令匹配器
    private ComboNode FindBestMatch(List<ComboNode> nodes, InputCmd triggerCmd, float holdTime)
    {
        ComboNode bestMatch = null;
        int maxSequenceLength = -1; // 用于优先级判定：要求越多的连招，优先级越高

        foreach (var node in nodes)
        {
            if (node == null) continue;

            // 1. 核对环境限制 (在地上还是天上？)
            if (node.castCondition == CastCondition.GroundOnly && !sm.IsGrounded()) continue;
            if (node.castCondition == CastCondition.AirOnly && sm.IsGrounded()) continue;

            // 2. 核对前置状态限制 (是不是在冲刺/滑铲？)
            if (!IsRequiredStateMet(node.requiredState)) continue;

            // 3. 核对蓄力条件
            if (node.isChargeSkill)
            {
                // 如果是蓄力技能，必须是松开按键触发(holdTime>0)，且时间必须达标
                if (holdTime < node.requiredChargeTime) continue;
            }
            else
            {
                // 如果是普通技能，只能由瞬间按下(holdTime==0)触发，防止松开蓄力时误打出普通攻击
                if (holdTime > 0f) continue;
            }

            // 4. 核对组合键序列
            if (node.inputSequence.Count == 0) continue;

            // 序列的最后一个键必须是当前按下的触发键 (如 LightAttack)
            if (node.inputSequence[node.inputSequence.Count - 1] != triggerCmd) continue;

            // 检查序列中前面的辅助方向键是否正被按住 (如 Up)
            bool isSequenceMatched = true;
            for (int i = 0; i < node.inputSequence.Count - 1; i++)
            {
                if (!IsDirectionalCommandHeld(node.inputSequence[i]))
                {
                    isSequenceMatched = false;
                    break;
                }
            }

            if (!isSequenceMatched) continue;

            // 5. 优先级对决：如果都满足，选序列最长的 (比如 Up+A 优先于 单按 A)
            if (node.inputSequence.Count > maxSequenceLength)
            {
                maxSequenceLength = node.inputSequence.Count;
                bestMatch = node;
            }
        }

        return bestMatch;
    }

    // ==================================================
    // 辅助验证器
    // ==================================================
    private bool IsRequiredStateMet(RequiredState req)
    {
        if (req == RequiredState.Any) return true;
        if (req == RequiredState.IdleOrRun) return (sm.currentState == sm.idleState || sm.currentState == sm.runState);
        if (req == RequiredState.Dash) return sm.currentState == sm.dashState;
        if (req == RequiredState.Slide) return sm.currentState == sm.slideState;
        if (req == RequiredState.Crouch) return sm.currentState == sm.crouchState;
        return false;
    }

    private bool IsDirectionalCommandHeld(InputCmd cmd)
    {
        // 向 Controller 的虚拟手柄索要当前的摇杆数据
        // (需确保你在 Flandre_NameSpace 的 InputCmd 里添加了 Up, Down, Left, Right)
        if (cmd == InputCmd.Up) return sm.playerController.moveInput.y > 0.1f;
        if (cmd == InputCmd.Down) return sm.playerController.moveInput.y < -0.1f;
        if (cmd == InputCmd.Left) return sm.playerController.moveInput.x < -0.1f;
        if (cmd == InputCmd.Right) return sm.playerController.moveInput.x > 0.1f;

        return false; // 其他非方向键一律返回 false
    }

    private void ExecuteNode()
    {
        hasBufferedInput = false;    // 消耗指令
        isGracePeriodActive = false; // 掐断宽限期读秒
        sm.ChangeState(sm.comboState);
    }

    public void StartGracePeriod()
    {
        isGracePeriodActive = true;
        graceTimer = 0f;
    }

    public void ResetCombo()
    {
        currentNode = null;
        isGracePeriodActive = false;
        hasBufferedInput = false;
    }

    private bool CheckIfHasShieldGem(InputCmd cmd)
    {
        if (loadout == null) return false;
        return loadout.HasShieldGem(cmd);
    }
}