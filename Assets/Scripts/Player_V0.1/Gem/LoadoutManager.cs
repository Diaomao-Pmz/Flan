using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem.Modules;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 装备中枢：芙兰的大脑记忆区，存储当前的流派配置，并负责转发指令
    /// </summary>
    public class LoadoutManager : MonoBehaviour
    {
        [Header("Global Keymodules (1主1副)")]
        // 主副模块的实例 (大招与全局被动，代码动态挂载)
        public IKeymodule MainSlot { get; private set; }
        public IKeymodule SubSlot { get; private set; }

        [Header("Action Gem Slots (动作强化插槽)")]
        [Tooltip("给跳跃分配的宝石")]
        public GemType jumpGemSlot = GemType.None;

        [Tooltip("给冲刺分配的宝石")]
        public GemType dashGemSlot = GemType.None;

        [Tooltip("给滑行分配的宝石")]
        public GemType slideGemSlot = GemType.None;

        // 动作强化插槽字典 (动作 -> 宝石) - 底层依然用字典，保证极速查询
        private Dictionary<ActionType, GemType> actionGemSlots = new Dictionary<ActionType, GemType>();

        // 引入角色上下文
        private PlayerController playerContext;

        private void Awake()
        {
            playerContext = GetComponent<PlayerController>();

            // 1. 初始化动作槽位，直接读取你在 Unity 面板里配好的宝石
            actionGemSlots[ActionType.Jump] = jumpGemSlot;
            actionGemSlots[ActionType.Dash] = dashGemSlot;
            actionGemSlots[ActionType.Slide] = slideGemSlot;
        }

        private void Update()
        {
            // 2. 驱动全局模块的状态更新
            MainSlot?.UpdateModule(Time.deltaTime);
            SubSlot?.UpdateModule(Time.deltaTime);
        }

        // ==========================================
        // 第一轨：主/副 技能模块管理 (全局能力)
        // ==========================================

        public void EquipMainModule(IKeymodule newModule)
        {
            MainSlot?.OnUnequip();   // 卸载旧模块，清除旧被动
            MainSlot = newModule;    // 替换新模块
            MainSlot?.OnEquip();     // 激活新被动
        }

        public void EquipSubModule(IKeymodule newModule)
        {
            SubSlot?.OnUnequip();
            SubSlot = newModule;
            SubSlot?.OnEquip();
        }

        public void TriggerMainActiveSkill()
        {
            if (MainSlot != null)
            {
                MainSlot.ExecuteActive();
            }
        }

        // ==========================================
        // 第二轨：动作强化 宝石管理 (局部修饰)
        // ==========================================

        /// <summary>
        /// 预留给未来 UI 界面镶嵌宝石调用的方法
        /// </summary>
        public void SetActionGem(ActionType action, GemType gem)
        {
            if (actionGemSlots.ContainsKey(action))
            {
                actionGemSlots[action] = gem;

                // 同步回 Inspector 面板显示，方便运行中调试查错
                if (action == ActionType.Jump) jumpGemSlot = gem;
                if (action == ActionType.Dash) dashGemSlot = gem;
                if (action == ActionType.Slide) slideGemSlot = gem;
            }
        }

        /// <summary>
        /// 【核心解耦点】FSM 状态机抛出钩子时，调用此方法！
        /// </summary>
        public void ExecuteActionModifier(ActionType action)
        {
            GemType equippedGem = actionGemSlots[action];

            if (equippedGem != GemType.None)
            {
                // 查阅到该动作镶嵌了宝石，呼叫静态处理器执行强化逻辑
                GemActionProcessor.ExecuteModifier(action, equippedGem, playerContext);
                Debug.Log($"[LoadoutManager] 触发动作强化: {action} 搭配了 {equippedGem} 宝石");
            }
        }

        /// <summary>
        /// 提供给卡带（如 SlideState）内部查询特定动作装备了什么宝石用
        /// </summary>
        public GemType GetGemForAction(ActionType action)
        {
            if (actionGemSlots.ContainsKey(action))
            {
                return actionGemSlots[action];
            }
            return GemType.None;
        }
    }
}