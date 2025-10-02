// File: SpectatorManager.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// SpectatorManager: gestiona el cambio local a modo espectador cuando el jugador es eliminado.
/// - Singleton (por cliente).
/// - Al entrar en modo espectador:
///    1) Desactiva componentes "control" del NetworkPlayer local (intenta no tocar NetworkPlayer).
///    2) Instancia una cámara "espectador" con control libre (FreeSpectatorCamera).
///    3) Muestra UI de espectador si está asignada.
/// - Guarda la lista de componentes desactivados para poder restaurar si sales de espectador.
/// 
/// Instrucciones:
///  - Asigna 'spectatorCameraPrefab' a un prefab que contenga Camera + FreeSpectatorCamera.
///  - Asigna 'spectatorUIPanel' a un GameObject de UI que quieras mostrar en modo espectador.
///  - El manager actúa localmente (Client-side). El servidor decide quién es espectador y notifica al client owner.
/// </summary>
public class SpectatorManager : MonoBehaviour
{
    public static SpectatorManager Instance;

    [Header("Prefabs / UI")]
    public GameObject spectatorCameraPrefab; // Prefab que contiene una Camera y el script FreeSpectatorCamera
    public GameObject spectatorUIPanel; // Panel UI para espectadores (opcional)

    private GameObject spawnedCamera;
    private List<MonoBehaviour> disabledComponents = new List<MonoBehaviour>();
    private bool isSpectating = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Llamar desde NotifyEliminatedClientRpc cuando el cliente local ha sido eliminado.
    /// Ejecuta los pasos para hacer al jugador espectador localmente.
    /// </summary>
    public void EnterSpectatorModeLocal()
    {
        if (isSpectating) return;
        isSpectating = true;

        // 1) Desactivar componentes "controladores" del player local
        DisableLocalPlayerControlComponents();

        // 2) Instanciar cámara espectador si hay prefab
        if (spectatorCameraPrefab != null)
        {
            spawnedCamera = Instantiate(spectatorCameraPrefab);
            // Asegurarse que la cámara nueva está activa y la principal queda desactivada (si es necesario)
            // También podrías posicionarla sobre la posición del jugador eliminado
            spawnedCamera.SetActive(true);
        }
        else
        {
            Debug.LogWarning("SpectatorManager: no se asignó spectatorCameraPrefab.");
        }

        // 3) Mostrar UI de espectador (si asignado)
        if (spectatorUIPanel != null) spectatorUIPanel.SetActive(true);

        Debug.Log("[SpectatorManager] Modo espectador activado localmente.");
    }

    /// <summary>
    /// Sale del modo espectador: restaura componentes desactivados y destruye cámara de espectador.
    /// </summary>
    public void ExitSpectatorModeLocal()
    {
        if (!isSpectating) return;
        isSpectating = false;

        // Reactivar componentes
        foreach (var c in disabledComponents)
            if (c != null) c.enabled = true;
        disabledComponents.Clear();

        // Destruir cámara spawneda
        if (spawnedCamera != null) Destroy(spawnedCamera);
        spawnedCamera = null;

        // Ocultar UI espectador
        if (spectatorUIPanel != null) spectatorUIPanel.SetActive(false);

        Debug.Log("[SpectatorManager] Modo espectador desactivado (local).");
    }

    /// <summary>
    /// Intenta desactivar componentes de control del jugador local.
    /// Este método busca el NetworkPlayer local (IsOwner) y desactiva todos los MonoBehaviours
    /// excepto NetworkPlayer y ManoJugadorUI y UIManager (para evitar romper UI).
    /// -- Si tienes componentes específicos de control (PlayerController, Movement, Input), puedes
    ///    añadirlos a la lista de 'exclusions' para que no se toquen.
    /// </summary>
    private void DisableLocalPlayerControlComponents()
    {
        // Buscar NetworkPlayer local
        NetworkPlayer local = null;
        foreach (var np in FindObjectsOfType<NetworkPlayer>())
        {
            if (np.IsOwner) { local = np; break; }
        }

        if (local == null)
        {
            Debug.LogWarning("SpectatorManager: no se encontró NetworkPlayer local para desactivar componentes.");
            return;
        }

        var comps = local.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            // No desactivar el NetworkPlayer en sí
            if (c is NetworkPlayer) continue;
            // No desactivar UI Manager ni scripts de UI importantes
            if (c is ManoJugadorUI) continue;
            // Puedes agregar más excepciones aquí si tienes componentes que deben seguir activos
            // e.g. if (c is SomeImportantComponent) continue;

            if (c.enabled)
            {
                c.enabled = false;
                disabledComponents.Add(c);
            }
        }

        // También desactivar la cámara local (si está en el player)
        Camera playerCam = local.GetComponentInChildren<Camera>();
        if (playerCam != null) playerCam.enabled = false;
    }
}
