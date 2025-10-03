// SpectatorManager.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SpectatorManager : MonoBehaviour
{
    public static SpectatorManager Instance;

    [Header("Prefabs / UI")]
    public GameObject spectatorCameraPrefab;
    public GameObject spectatorUIPanel;

    private GameObject spawnedCamera;
    private List<MonoBehaviour> disabledComponents = new List<MonoBehaviour>();
    private bool isSpectating = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void EnterSpectatorModeLocal()
    {
        if (isSpectating) return;
        isSpectating = true;
        DisableLocalPlayerControlComponents();

        if (spectatorCameraPrefab != null)
        {
            spawnedCamera = Instantiate(spectatorCameraPrefab);
            spawnedCamera.SetActive(true);
        }
        else Debug.LogWarning("SpectatorManager: no se asignó spectatorCameraPrefab.");

        if (spectatorUIPanel != null) spectatorUIPanel.SetActive(true);

        Debug.Log("[SpectatorManager] Modo espectador activado localmente.");
    }

    public void ExitSpectatorModeLocal()
    {
        if (!isSpectating) return;
        isSpectating = false;

        foreach (var c in disabledComponents) if (c != null) c.enabled = true;
        disabledComponents.Clear();

        if (spawnedCamera != null) Destroy(spawnedCamera);
        spawnedCamera = null;
        if (spectatorUIPanel != null) spectatorUIPanel.SetActive(false);

        Debug.Log("[SpectatorManager] Modo espectador desactivado (local).");
    }

    private void DisableLocalPlayerControlComponents()
    {
        NetworkPlayer local = null;
        foreach (var np in FindObjectsOfType<NetworkPlayer>()) { if (np.IsOwner) { local = np; break; } }
        if (local == null) { Debug.LogWarning("SpectatorManager: no se encontró NetworkPlayer local para desactivar componentes."); return; }

        var comps = local.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            if (c is NetworkPlayer) continue;
            if (c is ManoJugadorUI) continue;
            if (c.enabled) { c.enabled = false; disabledComponents.Add(c); }
        }

        Camera playerCam = local.GetComponentInChildren<Camera>();
        if (playerCam != null) playerCam.enabled = false;
    }
}
