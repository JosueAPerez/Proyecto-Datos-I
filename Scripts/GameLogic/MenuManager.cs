// MainMenuManager.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputNombre;
    public TMP_InputField inputIP;
    public Button botonHost;
    public Button botonClient;
    public Button botonServer;

    [Header("Config")]
    public string gameSceneName = "GameScene";
    public ushort port = 7777;

    void Start()
    {
        if (botonHost != null) botonHost.onClick.AddListener(OnHostClick);
        if (botonClient != null) botonClient.onClick.AddListener(OnClientClick);
        if (botonServer != null) botonServer.onClick.AddListener(OnServerClick);
    }

    void GuardarNombre()
    {
        string nombre = inputNombre != null ? inputNombre.text.Trim() : "";
        if (!string.IsNullOrEmpty(nombre)) GameSettings.NombreJugador = nombre;
    }

    void ConfigurarTransporte(string ip)
    {
        if (NetworkManager.Singleton == null) { Debug.LogError("NetworkManager.Singleton es null"); return; }
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = ip;
            transport.ConnectionData.Port = port;
        }
        else Debug.LogWarning("UnityTransport no encontrado en NetworkManager.Singleton");
    }

    void OnHostClick()
    {
        GuardarNombre();
        ConfigurarTransporte("0.0.0.0");
        if (NetworkManager.Singleton != null) DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.StartHost())
        {
            Debug.Log("Host iniciado");
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        else Debug.LogError("No se pudo iniciar Host");
    }

    void OnClientClick()
    {
        GuardarNombre();
        if (inputIP == null || string.IsNullOrEmpty(inputIP.text)) { Debug.LogWarning("Ingresa la IP del servidor."); return; }
        ConfigurarTransporte(inputIP.text);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.StartClient()) Debug.Log("Cliente iniciado");
        else Debug.LogError("No se pudo iniciar Cliente");
    }

    void OnServerClick()
    {
        GuardarNombre();
        ConfigurarTransporte("0.0.0.0");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.StartServer())
        {
            Debug.Log("Servidor iniciado");
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        else Debug.LogError("No se pudo iniciar Servidor");
    }
}

