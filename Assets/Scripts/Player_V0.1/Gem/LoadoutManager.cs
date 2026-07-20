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
        public IKeymodule MainSlot { get; private set; }
        public IKeymodule SubSlot { get; private set; }

        [Header("Action Gem Slots (动作强化插槽)")]
        public GemType jumpGemSlot = GemType.None;
        public GemType dashGemSlot = GemType.None;
        public GemType slideGemSlot = GemType.None;

        private Dictionary<ActionType, GemType> actionGemSlots = new Dictionary<ActionType, GemType>();
        private PlayerController playerContext;

        private void Awake()
        {
            playerContext = GetComponent<PlayerController>();

            actionGemSlots[ActionType.Jump] = jumpGemSlot;
            actionGemSlots[ActionType.Dash] = dashGemSlot;
            actionGemSlots[ActionType.Slide] = slideGemSlot;
        }

        private void Update()
        {
            MainSlot?.UpdateModule(Time.deltaTime);
            SubSlot?.UpdateModule(Time.deltaTime);
        }

        // ==========================================
        // 第一轨：主/副 技能模块管理 (全局能力)
        // ==========================================
        public void EquipMainModule(IKeymodule newModule)
        {
            MainSlot?.OnUnequip();
            MainSlot = newModule;
            MainSlot?.OnEquip();
        }

        public void EquipSubModule(IKeymodule newModule)
        {
            SubSlot?.OnUnequip();
            SubSlot = newModule;
            SubSlot?.OnEquip();
        }

        public void TriggerMainActiveSkill()
        {
            if (MainSlot != null) MainSlot.ExecuteActive();
        }

        // ==========================================
        // 第二轨：动作强化 宝石管理 (局部修饰)
        // ==========================================
        public void SetActionGem(ActionType action, GemType gem)
        {
            if (actionGemSlots.ContainsKey(action))
            {
                actionGemSlots[action] = gem;

                if (action == ActionType.Jump) jumpGemSlot = gem;
                if (action == ActionType.Dash) dashGemSlot = gem;
                if (action == ActionType.Slide) slideGemSlot = gem;
            }
        }

        public void ExecuteActionModifier(ActionType action)
        {
            GemType equippedGem = actionGemSlots[action];
            if (equippedGem != GemType.None)
            {
                GemActionProcessor.ExecuteModifier(action, equippedGem, playerContext);
                Debug.Log($"[LoadoutManager] 触发动作强化: {action} 搭配了 {equippedGem} 宝石");
            }
        }

        public GemType GetGemForAction(ActionType action)
        {
            if (actionGemSlots.ContainsKey(action)) return actionGemSlots[action];
            return GemType.None;
        }

        // ==========================================
        // 【新增】：专为受身打断 (Combo Breaker) 提供的查询接口
        // ==========================================
        public bool HasShieldGem(InputCmd cmd)
        {
            ActionType mappedAction;

            // 将输入的按键指令，映射到对应的动作插槽上
            switch (cmd)
            {
                case InputCmd.Jump: mappedAction = ActionType.Jump; break;
                case InputCmd.Dash: mappedAction = ActionType.Dash; break;
                case InputCmd.Slide: mappedAction = ActionType.Slide; break;
                default: return false; // 攻击键或其他按键直接驳回
            }

            // 查询该动作是否装备了 Shield 宝石
            return GetGemForAction(mappedAction) == GemType.Shield;
        }
    }
}