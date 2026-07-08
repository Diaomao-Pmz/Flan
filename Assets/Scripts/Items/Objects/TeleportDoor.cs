using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TeleportDoor : MonoBehaviour, IInteractable
{
    [Header("传送设置")]
    public Transform destination;     // 目标房间的出生点坐标

    [Header("视觉表现")]
    [Tooltip("勾选：传送门自己播放黑屏渐变\n不勾选：瞬间传送（专为配合过场动画/剧情脚本设计）")]
    public bool useFadeEffect = true; // 核心开关！

    [Tooltip("如果不勾选上面的开关，这个空着也没关系")]
    public Image fadeImage;
    public float fadeTime = 0.5f;

    private bool isTeleporting = false;
    private GameObject player;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
    }

    // 实现了 IInteractable 接口的 Interact 方法
    public void Interact()
    {
        if (!isTeleporting)
        {
            if (useFadeEffect)
            {
                // 模式 A：启动老字号的黑屏传送魔法
                StartCoroutine(TeleportWithFade());
            }
            else
            {
                // 模式 B：极其干脆的瞬间传送！把视觉表现交给其他大佬
                InstantTeleport();
            }
        }
    }

    // ==========================================
    // 模式 A 的协程：带黑幕的传送
    // ==========================================
    private IEnumerator TeleportWithFade()
    {
        isTeleporting = true;

        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
            Color color = fadeImage.color;

            // 1. 渐渐变黑
            float timer = 0f;
            while (timer < fadeTime)
            {
                timer += Time.deltaTime;
                color.a = Mathf.Lerp(0f, 1f, timer / fadeTime);
                fadeImage.color = color;
                yield return null;
            }
        }

        // 2. 调用核心传送逻辑！
        PerformPhysicalTeleport();

        if (fadeImage != null)
        {
            // 3. 让黑屏保持 0.2 秒，营造空间转换感
            yield return new WaitForSeconds(0.2f);

            // 4. 渐渐变亮
            float timer = 0f;
            Color color = fadeImage.color;
            while (timer < fadeTime)
            {
                timer += Time.deltaTime;
                color.a = Mathf.Lerp(1f, 0f, timer / fadeTime);
                fadeImage.color = color;
                yield return null;
            }
            fadeImage.gameObject.SetActive(false);
        }

        isTeleporting = false;
    }

    // ==========================================
    // 模式 B 的方法：瞬间传送
    // ==========================================
    private void InstantTeleport()
    {
        PerformPhysicalTeleport();
    }

    // ==========================================
    // 底层物理操作：改变坐标（被上面两种模式共用）
    // ==========================================
    private void PerformPhysicalTeleport()
    {
        // 瞬间把玩家挪过去
        player.transform.position = destination.position;

        // 强制让主摄像机也瞬间切过去，防止它在黑屏/切镜里缓慢滑过虚空
        Camera.main.transform.position = new Vector3(
            destination.position.x,
            destination.position.y,
            Camera.main.transform.position.z
        );
    }
}