using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera mainCamera;

    private void Start()
    {
        // Find the main active top-down camera in the scene
        mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        // Align the child sprite's rotation directly with the camera's rotation
        transform.rotation = mainCamera.transform.rotation;
    }
}