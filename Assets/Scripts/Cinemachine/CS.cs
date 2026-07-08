using UnityEngine;
using Unity.Cinemachine;

public class CameraSimpleSwitch : MonoBehaviour
{
    public CinemachineCamera targetCamera;

    // 这个 public 方法就是专门留给接口调用的
    public void TurnOnCamera()
    {
        targetCamera.Priority = 20; // 调高优先级，夺取画面
    }

    public void TurnOffCamera()
    {
        targetCamera.Priority = 0;  // 降回优先级，交还画面
    }
}