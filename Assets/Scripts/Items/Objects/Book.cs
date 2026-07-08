using UnityEngine;
using TMPro; // 引入 TextMeshPro 的命名空间

// 必须继承 MonoBehaviour 才能挂载到物体上，同时接入 IInteractable 插座
public class Book : MonoBehaviour, IInteractable
{
    [Header("UI 连线")]
    public GameObject readPanel;          // 整个暗幕和纸张的父物体
    public TextMeshProUGUI paperText;     // 纸张上的文字组件

    [Header("书籍内容")]
    [TextArea(3, 10)] // 让面板里输入文字的框变大，方便你写长篇大论
    public string bookContent;

    private bool isReading = false;       // 状态标记

    // 实现 IInteractable 接口的 Interact 方法（按下E时触发）
    public void Interact()
    {
        if (!isReading)
        {
            OpenBook();
        }
    }

    void Update()
    {
        // 如果正在阅读中，监听退出按键 (用 Escape 或 Q 键退出，防止和E键冲突)
        if (isReading)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q))
            {
                CloseBook();
            }
        }
    }

    private void OpenBook()
    {
        isReading = true;

        // 1. 替换文字内容
        paperText.text = bookContent;

        // 2. 显示UI
        readPanel.SetActive(true);

        // 3. 终极魔法：时间暂停！(这会让所有基于物理和帧的移动瞬间定住)
        Time.timeScale = 0f;
    }

    private void CloseBook()
    {
        isReading = false;

        // 1. 隐藏UI
        readPanel.SetActive(false);

        // 2. 恢复时间流动
        Time.timeScale = 1f;
    }
}