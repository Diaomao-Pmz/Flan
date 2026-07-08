using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

public class ComboInputBuffer : MonoBehaviour
{
    [Header("连招树根节点 (起手招式)")]
    public List<ComboNode> rootNodes = new List<ComboNode>();

    // 核心状态：当前打到了哪一招
    public ComboNode currentNode { get; private set; }
    private float lastInputTime;

    private PlayerStateMachine sm;

    void Awake()
    {
        sm = GetComponent<PlayerStateMachine>();
    }

    // 唯一的对外入口，接收 PlayerController 传来的抽象指令
    public void OnReceiveInput(InputCmd cmd)
    {
        // ==========================================
        // 1. 非打断指令 - 放行且保留连招进度
        // ==========================================
        if (cmd == InputCmd.Jump || cmd == InputCmd.Dash || cmd == InputCmd.Slide)
        {
            if (cmd == InputCmd.Dash && sm.dashSkill.CanExecute())
                sm.ChangeState(sm.dashState);

            if (cmd == InputCmd.Slide && sm.slideSkill.CanExecute())
                sm.ChangeState(sm.slideState);

            if (cmd == InputCmd.Jump && sm.jumpCount < sm.maxJumps)
                sm.ChangeState(sm.jumpState);

            // 提前 return，【绝对不修改 currentNode】，连招被完美挂起！
            return;
        }

        // ==========================================
        // 2. 攻击类指令 - 在树上对暗号
        // ==========================================
        if (currentNode == null)
        {
            // 尝试起手：从根节点中找匹配的
            currentNode = rootNodes.Find(x => x.inputRequirement == cmd);
            if (currentNode != null) ExecuteNode();
        }
        else
        {
            // 检查是否还在派生窗口期内
            if (Time.time - lastInputTime <= currentNode.comboWindow)
            {
                // 在当前招式的子节点里，找匹配按键的下一招
                ComboNode nextNode = currentNode.childNodes.Find(x => x.inputRequirement == cmd);

                if (nextNode != null)
                {
                    currentNode = nextNode; // 连招成功推进！
                    ExecuteNode();
                }
                else
                {
                    ResetCombo(); // 按错了键（或者没有这个派生），连招断掉
                }
            }
            else
            {
                // 超时了，连招断掉，但顺便检查当前按键能不能作为新一轮的起手
                ResetCombo();
                currentNode = rootNodes.Find(x => x.inputRequirement == cmd);
                if (currentNode != null) ExecuteNode();
            }
        }
    }

    private void ExecuteNode()
    {
        lastInputTime = Time.time;
        // 强制状态机进入统一的 ComboState
        sm.ChangeState(sm.comboState);
    }

    public void ResetCombo()
    {
        currentNode = null;
    }
}