using UnityEngine;
using UnityEngine.Events; // 核心魔法：引入 Unity 事件系统

public class InteractableObject : MonoBehaviour
{
    [Header("交互设置")]
    [Tooltip("玩家需要按下的按键")]
    public KeyCode interactKey = KeyCode.E;

    [Header("自定义触发事件 (面板连线)")]
    [Tooltip("当玩家在范围内按下交互键时，会执行列表里的所有事情")]
    public UnityEvent onInteract;

    // 内部状态：记录玩家是否站在触发器里
    private bool isPlayerInRange = false;

    private void Update()
    {
        // 只有当玩家在范围内，并且按下了指定的按键时，才会触发
        if (isPlayerInRange && Input.GetKeyDown(interactKey))
        {
            // 呼叫总机：执行你在 Inspector 面板里配置的所有事件！
            onInteract.Invoke();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 玩家进入范围，挂起“可交互”状态
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
            // 进阶提示：你以后可以在这里写代码，头顶冒出一个 "按 E 交互" 的气泡
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // 玩家离开范围，取消“可交互”状态
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;
        }
    }
}