using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine.Rendering.Universal;

public class CameraSwitchTrigger : MonoBehaviour
{
    [Header("设置需要切换的摄像机")]
    public CinemachineCamera currentCamera;
    public CinemachineCamera nextCamera;

    [Header("像素完美组件")]
    public PixelPerfectCamera pixelPerfectCamera;
    public float blendTimeBackToPlayer = 2f;

    [Header("触发模式选择")]
    [Tooltip("勾选：走进去自动切换。 不勾选：必须配合按键交互组件触发")]
    public bool autoTriggerOnEnter = true;

    // 内部状态
    private bool isViewingTarget = false;

    // ==========================================
    // 1. 自动触发逻辑 (走进去触发)
    // ==========================================
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 只有在勾选了自动触发，且碰到的是玩家时才执行
        if (autoTriggerOnEnter && other.CompareTag("Player"))
        {
            SwitchToTarget();
        }
    }

    // 无论哪种模式，走出去都自动切回玩家
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") && isViewingTarget)
        {
            SwitchBackToPlayer();
        }
    }

    // ==========================================
    // 2. 被动调用接口 (给按 E 键交互准备的)
    // ==========================================
    public void ToggleCamera()
    {
        if (isViewingTarget) SwitchBackToPlayer();
        else SwitchToTarget();
    }

    // ==========================================
    // 核心切换魔法 (不要重复写代码，封装起来)
    // ==========================================
    private void SwitchToTarget()
    {
        isViewingTarget = true;
        if (pixelPerfectCamera != null) pixelPerfectCamera.enabled = false;
        nextCamera.Priority = 11;
        currentCamera.Priority = 10;
    }

    private void SwitchBackToPlayer()
    {
        isViewingTarget = false;
        nextCamera.Priority = 10;
        currentCamera.Priority = 11;
        StartCoroutine(EnablePixelPerfectAfterBlend());
    }

    private IEnumerator EnablePixelPerfectAfterBlend()
    {
        yield return new WaitForSeconds(blendTimeBackToPlayer);
        if (pixelPerfectCamera != null && !isViewingTarget)
            pixelPerfectCamera.enabled = true;
    }
}