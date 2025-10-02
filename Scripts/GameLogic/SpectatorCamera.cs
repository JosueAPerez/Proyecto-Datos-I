// File: FreeSpectatorCamera.cs
using UnityEngine;

/// <summary>
/// Control simple de cámara libre para espectador:
/// - WASD para moverse en el plano local, QE para subir/bajar
/// - Mouse para rotación (hold right mouse to look)
/// - Shift para mover más rápido
/// </summary>
[RequireComponent(typeof(Camera))]
public class FreeSpectatorCamera : MonoBehaviour
{
    public float moveSpeed = 8f;
    public float fastSpeedMultiplier = 3f;
    public float mouseSensitivity = 2f;
    private float yaw = 0f;
    private float pitch = 0f;

    void Start()
    {
        var cam = GetComponent<Camera>();
        if (cam == null) Debug.LogWarning("FreeSpectatorCamera requiere una Camera en el prefab.");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        HandleMouse();
        HandleMovement();
    }

    private void HandleMouse()
    {
        if (Input.GetMouseButton(1)) // mirar con botón derecho
        {
            float mx = Input.GetAxis("Mouse X");
            float my = -Input.GetAxis("Mouse Y");
            yaw += mx * mouseSensitivity;
            pitch += my * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.localEulerAngles = new Vector3(pitch, yaw, 0f);
        }
    }

    private void HandleMovement()
    {
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastSpeedMultiplier : 1f);
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 up = transform.up;

        Vector3 move = Vector3.zero;
        move += forward * Input.GetAxis("Vertical");
        move += right * Input.GetAxis("Horizontal");
        if (Input.GetKey(KeyCode.E)) move += up;
        if (Input.GetKey(KeyCode.Q)) move -= up;

        transform.position += move * speed * Time.deltaTime;
    }
}
