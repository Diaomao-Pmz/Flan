using UnityEngine;
using Flandre.CombatSystem.Modules;

namespace Flandre.CombatSystem.Keymodules
{
    public class EchoModule : IKeymodule
    {
        public string ModuleName => "Echo (回响)";

        // 【修复1】：将原本单一的 player 拆分为控制(controller)和数据(state)
        private PlayerController controller;
        private PlayerState state;

        private bool isRecording = false;
        private float recordTimer = 0f;
        private const float MaxRecordTime = 1.0f;

        private Vector3 savedPosition;
        private Vector2 savedVelocity;
        private int savedFacingDirection;

        // 构造函数
        public EchoModule(PlayerController playerContext)
        {
            this.controller = playerContext;
            // 【修复2】：通过传入的 controller，获取挂载在同一个物体上的 PlayerState
            // 这样就不用去修改 LoadoutManager 里的实例化代码了
            this.state = playerContext.GetComponent<PlayerState>();
        }

        public void OnEquip()
        {
            Debug.Log($"[{ModuleName}] 已装备：激活全局被动。");
            // 【修复3】：属性相关的数据，现在去 state 里面找
            state.stats.cooldownReduction += 0.15f;
            state.combat.comboWindowTolerance += 0.2f;
        }

        public void OnUnequip()
        {
            Debug.Log($"[{ModuleName}] 已卸载：注销全局被动。");
            // 撤销属性加成
            state.stats.cooldownReduction -= 0.15f;
            state.combat.comboWindowTolerance -= 0.2f;
        }

        public void UpdateModule(float deltaTime)
        {
            if (isRecording)
            {
                recordTimer -= deltaTime;
                if (recordTimer <= 0f)
                {
                    isRecording = false;
                    Debug.Log($"[{ModuleName}] 1秒时间到，回响记录消散。");
                }
            }
        }

        public void ExecuteActive()
        {
            if (!isRecording)
            {
                // 【修复4】：位置、物理、朝向等行为控制，去 controller 里面找
                savedPosition = controller.transform.position;
                savedVelocity = controller.rb.linearVelocity;
                savedFacingDirection = controller.facingDirection;

                isRecording = true;
                recordTimer = MaxRecordTime;

                Debug.Log($"[{ModuleName}] 第一段释放：已记录状态！1秒内可重放！");
            }
            else
            {
                // 重放动作
                controller.transform.position = savedPosition;
                controller.rb.linearVelocity = savedVelocity;
                controller.SetFacingDirection(savedFacingDirection);

                isRecording = false;
                Debug.Log($"[{ModuleName}] 第二段释放：动作再现！");
            }
        }
    }
}